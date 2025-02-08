#include <mono/metadata/appdomain.h>
#include <mono/metadata/class.h>
#include <mono/metadata/debug-helpers.h>
#include <mono/metadata/threads.h>

#include "monodroid-glue-internal.hh"
#include "mono-image-loader.hh"

using namespace xamarin::android::internal;

static constexpr char Unknown[] = "Unknown";

const char*
MonodroidRuntime::get_method_name (uint32_t mono_image_index, uint32_t method_token) noexcept
{
	uint64_t id = (static_cast<uint64_t>(mono_image_index) << 32) | method_token;

	log_debug (LOG_ASSEMBLY, "MM: looking for name of method with id {:x}, in mono image at index {}", id, mono_image_index);
	size_t i = 0uz;
	while (mm_method_names[i].id != 0) {
		if (mm_method_names[i].id == id) {
			return mm_method_names[i].name;
		}
		i++;
	}

	return Unknown;
}

const char*
MonodroidRuntime::get_class_name (uint32_t class_index) noexcept
{
	if (class_index >= marshal_methods_number_of_classes) {
		return Unknown;
	}

	return mm_class_names[class_index];
}

template<bool NeedsLocking>
force_inline void
MonodroidRuntime::get_function_pointer (uint32_t mono_image_index, uint32_t class_index, uint32_t method_token, void*& target_ptr) noexcept
{
	log_debug (
		LOG_ASSEMBLY,
		"MM: Trying to look up pointer to method '{}' (token {:x}) in class '{}' (index {})",
		optional_string (get_method_name (mono_image_index, method_token)), method_token,
		optional_string (get_class_name (class_index)), class_index
	);

	if (class_index >= marshal_methods_number_of_classes) [[unlikely]] {
		Helpers::abort_application (
			std::format (
				"Internal error: invalid index for class cache (expected at most {}, got {})",
				marshal_methods_number_of_classes - 1,
				class_index
			)
		);
	}

	// We need to do that, as Mono APIs cannot be invoked from threads that aren't attached to the runtime.
	mono_jit_thread_attach (mono_get_root_domain ());

	MonoImage *image = MonoImageLoader::get_from_index (mono_image_index);
	MarshalMethodsManagedClass &klass = marshal_methods_class_cache[class_index];
	if (klass.klass == nullptr) {
		klass.klass = image != nullptr ? mono_class_get (image, klass.token) : nullptr;
	}

	MonoMethod *method = klass.klass != nullptr ? mono_get_method (image, method_token, klass.klass) : nullptr;
	MonoError error;
	void *ret = method != nullptr ? mono_method_get_unmanaged_callers_only_ftnptr (method, &error) : nullptr;

	if (ret != nullptr) [[likely]] {
		if constexpr (NeedsLocking) {
			__atomic_store_n (&target_ptr, ret, __ATOMIC_RELEASE);
		} else {
			target_ptr = ret;
		}

		log_debug (
			LOG_ASSEMBLY,
			"Loaded pointer to method {} ({:p}) (mono_image_index == {}; class_index == {}; method_token == {:x})",
			optional_string (mono_method_full_name (method, true)),
			ret,
			mono_image_index,
			class_index,
			method_token
		);
		return;
	}

	log_fatal (
		LOG_DEFAULT,
		"Failed to obtain function pointer to method '{}' in class '{}'",
		optional_string (get_method_name (mono_image_index, method_token)),
		optional_string (get_class_name (class_index))
	);

	log_fatal (
		LOG_DEFAULT,
		"Looked for image index {}, class index {}, method token {:x}",
		mono_image_index,
		class_index,
		method_token
	);

	if (image == nullptr) {
		log_fatal (LOG_DEFAULT, "Failed to load MonoImage for the assembly"sv);
	} else if (method == nullptr) {
		log_fatal (LOG_DEFAULT, "Failed to load class from the assembly"sv);
	}

	const char *message = nullptr;
	if (error.error_code != MONO_ERROR_NONE) {
		message = mono_error_get_message (&error);
	}

	Helpers::abort_application (
		message == nullptr ? "Failure to obtain marshal methods function pointer"sv : message
	);
}

void
MonodroidRuntime::get_function_pointer_at_startup (uint32_t mono_image_index, uint32_t class_index, uint32_t method_token, void*& target_ptr) noexcept
{
	get_function_pointer<false> (mono_image_index, class_index, method_token, target_ptr);
}

void
MonodroidRuntime::get_function_pointer_at_runtime (uint32_t mono_image_index, uint32_t class_index, uint32_t method_token, void*& target_ptr) noexcept
{
	get_function_pointer<true> (mono_image_index, class_index, method_token, target_ptr);
}

get_function_pointer_fn
MonodroidRuntime::get_managed_marshal_methods_lookup_uco () noexcept
{
	if (default_alc == nullptr) {
		Helpers::abort_application ("The default assembly load context is not set"sv);
	}

	MonoAssembly *mono_android_assembly = Util::monodroid_load_assembly (default_alc, SharedConstants::MONO_ANDROID_ASSEMBLY_NAME.data ());
	MonoImage *mono_android_image = mono_assembly_get_image (mono_android_assembly);

	MonoClass *managed_marshal_methods_lookup_table_class = mono_class_from_name (mono_android_image, "Java.Interop", "ManagedMarshalMethodsLookupTable");
	if (managed_marshal_methods_lookup_table_class == nullptr) {
		Helpers::abort_application ("The Java.Interop.ManagedMarshalMethodsLookupTable class could not be found in Mono.Android"sv);
	}

	MonoMethod *get_function_pointer_method = mono_class_get_method_from_name (managed_marshal_methods_lookup_table_class, "GetFunctionPointer", 4);
	if (get_function_pointer_method == nullptr) {
		Helpers::abort_application ("The Java.Interop.ManagedMarshalMethodsLookupTable.GetFunctionPointer method could not be found"sv);
	}

	MonoError error;
	auto get_function_pointer_uco = reinterpret_cast<get_function_pointer_fn> (mono_method_get_unmanaged_callers_only_ftnptr (get_function_pointer_method, &error));

	if (error.error_code != MONO_ERROR_NONE) {
		const char *message = mono_error_get_message (&error);
		Helpers::abort_application (
			message == nullptr ? "Failure to obtain Java.Interop.FunctionPointerResolver.InitializeFunctionPointer UCO method"sv : message
		);
	}

	return get_function_pointer_uco;
}

void
MonodroidRuntime::managed_marshal_method_lookup (uint32_t assembly_index, uint32_t class_index, uint32_t method_index, void*& target_ptr) noexcept
{
	static get_function_pointer_fn get_function_pointer_uco;

	if (get_function_pointer_uco == nullptr) {
		get_function_pointer_uco = get_managed_marshal_methods_lookup_uco ();
	}

	get_function_pointer_uco (assembly_index, class_index, method_index, target_ptr);
}
