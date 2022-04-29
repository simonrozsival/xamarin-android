using System;
using System.Net;
using System.Text;

using Java.Net;

namespace Xamarin.Android.Net
{
	internal sealed class AuthModuleNtlm : IAndroidAuthenticationModule
	{
		public AuthenticationScheme Scheme { get; } = AuthenticationScheme.Ntlm;
		public string AuthenticationType { get; } = "NTLM";
		public bool CanPreAuthenticate { get; } = true;

		public Authorization? Authenticate (string challenge, HttpURLConnection request, ICredentials credentials)
			// => AuthChallengeResponseHelper.Authenticate (AuthenticationType, challenge, request, credentials);
			=> null; // TODO I've diverged from the AuthModule* way of doing things with the wrapper message handler... and now I'm not sure if I can implement the Authenticate method

		public Authorization? PreAuthenticate (HttpURLConnection request, ICredentials credentials)
			=> null;
	}
}
