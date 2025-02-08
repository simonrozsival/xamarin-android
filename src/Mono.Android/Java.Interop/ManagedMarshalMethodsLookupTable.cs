using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Android.Runtime;

namespace Java.Interop;

internal class ManagedMarshalMethodsLookupTable
{
	[UnmanagedCallersOnly]
	static unsafe void GetFunctionPointer (int assemblyIndex, int classIndex, int methodIndex, IntPtr target)
	{
		try
		{
			if (target == IntPtr.Zero)
			{
				throw new InvalidOperationException ("Invalid target memory location for GetFunctionPointer");
			}

			// TODO is this an atomic operation? in the "NeedsLocking" C++ code, we use __atomic_store_n (&target_ptr, ret, __ATOMIC_RELEASE) to write the fnptr
			IntPtr result = GetFunctionPointer (assemblyIndex, classIndex, methodIndex);

			if (result == IntPtr.Zero || result == (IntPtr)(-1))
			{
				throw new InvalidOperationException ($"Failed to get function pointer for ({assemblyIndex}, {classIndex}, {methodIndex})");
			}

			*(IntPtr*)target = result;
		}
		catch (Exception ex)
		{
			// Logger.Log (LogLevel.Error, "monodroid", $"Error storing function pointer: {ex}");
			AndroidEnvironment.UnhandledException (ex);
		}
	}

	static IntPtr GetFunctionPointer (int assemblyIndex, int classIndex, int methodIndex)
	{
		// ManagedMarshalMethodsLookupGenerator generates the body of this method is generated at app build time
		throw new NotImplementedException ();
	}
}
