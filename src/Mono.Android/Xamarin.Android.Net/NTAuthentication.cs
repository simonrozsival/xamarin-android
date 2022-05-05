using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Reflection;
using System.Runtime;
using System.Security.Authentication.ExtendedProtection;

namespace Xamarin.Android.Net
{
	internal sealed class NTAuthentication
	{
		internal enum ContextFlagsPal
		{
			None = 0,
			Delegate = 0x00000001,
			MutualAuth = 0x00000002,
			ReplayDetect = 0x00000004,
			SequenceDetect = 0x00000008,
			Confidentiality = 0x00000010,
			UseSessionKey = 0x00000020,
			AllocateMemory = 0x00000100,
			Connection = 0x00000800,
			InitExtendedError = 0x00004000,
			AcceptExtendedError = 0x00008000,
			InitStream = 0x00008000,
			AcceptStream = 0x00010000,
			InitIntegrity = 0x00010000,
			AcceptIntegrity = 0x00020000,
			InitManualCredValidation = 0x00080000,
			InitUseSuppliedCreds = 0x00000080,
			InitIdentify = 0x00020000,
			AcceptIdentify = 0x00080000,
			ProxyBindings = 0x04000000,
			AllowMissingBindings = 0x10000000,
			UnverifiedTargetName = 0x20000000,
		}

        //private const string AssemblyName = "System.Net.Http";
        //private const string TypeName = "System.Net.NTAuthentication";
        //private const string ContextFlagsPalTypeName = "System.Net.ContextFlagsPal";
        private const string AssemblyName = "Mono.Android";
        private const string TypeName = "Xamarin.Android.Net.TEMPORARY.NTAuthentication";
        private const string ContextFlagsPalTypeName = "Xamarin.Android.Net.TEMPORARY.ContextFlagsPal";
        private const BindingFlags InstanceBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		private static Lazy<Type> s_NTAuthenticationType = new Lazy<Type>(() => FindType(TypeName, AssemblyName));
		private static Lazy<PropertyInfo> s_IsCompletedPropertyInfo = new Lazy<PropertyInfo>(() => GetProperty("IsCompleted"));
		private static Lazy<MethodInfo> s_GetOutgoingBlobMethodInfo = new Lazy<MethodInfo>(() => GetMethod("GetOutgoingBlob"));
		private static Lazy<MethodInfo> s_CloseContextMethodInfo = new Lazy<MethodInfo>(() => GetMethod("CloseContext"));

		private static Type FindType(string typeName, string assemblyName)
			=> Type.GetType($"{typeName}, {assemblyName}", throwOnError: true)!; // TODO really throw? is there some better fallback?

		// private static ConstructorInfo GetNTAuthenticationConstructor()
		// 	=> s_NTAuthenticationType.Value.GetConstructor(
		// 		InstanceBindingFlags,
		// 		new[]
		// 		{
		// 			typeof(bool),
		// 			typeof(string),
		// 			typeof(NetworkCredential),
		// 			typeof(string),
		// 			FindType(ContextFlagsPalTypeName, AssemblyName),
		// 			typeof(ChannelBinding)
		// 		}) ?? throw new Exception($"Type {TypeName} is missing constructor"); // TODO don't use Exception

		private static PropertyInfo GetProperty(string name)
			=> s_NTAuthenticationType.Value.GetProperty(name, InstanceBindingFlags) ?? throw new Exception($"Type {TypeName} is missing property {name}"); // TODO don't use Exception

		private static MethodInfo GetMethod(string name)
			=> s_NTAuthenticationType.Value.GetMethod(name, InstanceBindingFlags) ?? throw new Exception($"Type {TypeName} is missing method {name}"); // TODO don't use Exception

		private object _instance;

		// TODO fix the dynamic dependency
		[DynamicDependency("#ctor(System.String,System.Net.NetworkCredential)", TypeName, AssemblyName)]
		internal NTAuthentication (bool isServer, string package, NetworkCredential credential, string? spn, ContextFlagsPal requestedContextFlags, ChannelBinding? channelBinding)
		{
			_instance = Activator.CreateInstance(s_NTAuthenticationType.Value, new object?[] { isServer, package, credential, spn, (int)requestedContextFlags, channelBinding });
		}

		public bool IsCompleted
			=> GetIsCompleted();

		[DynamicDependency("get_IsCompleted", TypeName, AssemblyName)]
		private bool GetIsCompleted()
			=> (bool)(s_IsCompletedPropertyInfo.Value.GetValue(_instance) ?? throw new Exception("TODO")); // TODO don't use Exception

		[DynamicDependency("GetOutgoingBlob", TypeName, AssemblyName)]
		public string? GetOutgoingBlob(string? incomingBlob)
			=> (string?)s_GetOutgoingBlobMethodInfo.Value.Invoke(_instance, new object?[] { incomingBlob });

		[DynamicDependency("CloseContext", TypeName, AssemblyName)]
		public void CloseContext()
			=> s_CloseContextMethodInfo.Value.Invoke(_instance, null);
	}
}
