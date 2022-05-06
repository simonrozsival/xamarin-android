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

		private const string IsCompletedPropertyName = "IsCompleted";
		private const string GetOutgoingBlobMethodName = "GetOutgoingBlob";
		private const string CloseContextMethodName = "CloseContext";

		private const BindingFlags InstanceBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		private static Lazy<Type> s_NTAuthenticationType = new Lazy<Type>(() => FindType(TypeName, AssemblyName));
		private static Lazy<ConstructorInfo> s_NTAuthenticationConstructorInfo = new Lazy<ConstructorInfo>(() => GetNTAuthenticationConstructor());
		private static Lazy<PropertyInfo> s_IsCompletedPropertyInfo = new Lazy<PropertyInfo>(() => GetProperty(IsCompletedPropertyName));
		private static Lazy<MethodInfo> s_GetOutgoingBlobMethodInfo = new Lazy<MethodInfo>(() => GetMethod(GetOutgoingBlobMethodName));
		private static Lazy<MethodInfo> s_CloseContextMethodInfo = new Lazy<MethodInfo>(() => GetMethod(CloseContextMethodName));

		private static Type FindType(string typeName, string assemblyName)
			=> Type.GetType($"{typeName}, {assemblyName}", throwOnError: true)!; // TODO really throw? is there some better fallback?

		private static ConstructorInfo GetNTAuthenticationConstructor()
			=> s_NTAuthenticationType.Value.GetConstructor(
				InstanceBindingFlags,
				new[]
				{
					typeof(bool),
					typeof(string),
					typeof(NetworkCredential),
					typeof(string),
					FindType(ContextFlagsPalTypeName, AssemblyName),
					typeof(ChannelBinding)
				}) ?? throw new MissingMemberException(TypeName, ConstructorInfo.ConstructorName);

		private static PropertyInfo GetProperty(string name)
			=> s_NTAuthenticationType.Value.GetProperty(name, InstanceBindingFlags) ?? throw new MissingMemberException(TypeName, name);

		private static MethodInfo GetMethod(string name)
			=> s_NTAuthenticationType.Value.GetMethod(name, InstanceBindingFlags) ?? throw new MissingMemberException(TypeName, name);

		private object _instance;

		[DynamicDependency("#ctor(System.Boolean,System.String,System.Net.NetworkCredential,System.String,System.Net.ContextFlagsPal,System.Security.Authentication.ExtendedProtection.ChannelBinding)", TypeName, AssemblyName)]
		internal NTAuthentication (bool isServer, string package, NetworkCredential credential, string? spn, int requestedContextFlags, ChannelBinding? channelBinding)
		{
			var constructorParams = new object?[] { isServer, package, credential, spn, requestedContextFlags, channelBinding };
			_instance = s_NTAuthenticationConstructorInfo.Value.Invoke(constructorParams);
		}

		public bool IsCompleted
			=> GetIsCompleted();

		[DynamicDependency($"get_{IsCompletedPropertyName}", TypeName, AssemblyName)]
		private bool GetIsCompleted()
			=> (bool)s_IsCompletedPropertyInfo.Value.GetValue(_instance);

		[DynamicDependency(GetOutgoingBlobMethodName, TypeName, AssemblyName)]
		public string? GetOutgoingBlob(string? incomingBlob)
			=> (string?)s_GetOutgoingBlobMethodInfo.Value.Invoke(_instance, new object?[] { incomingBlob });

		[DynamicDependency(CloseContextMethodName, TypeName, AssemblyName)]
		public void CloseContext()
			=> s_CloseContextMethodInfo.Value.Invoke(_instance, null);
	}
}
