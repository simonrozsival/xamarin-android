using System;
using System.Net;
using System.Text;

using Java.Net;

namespace Xamarin.Android.Net
{
	internal sealed class AuthModuleNegotiate : IAndroidAuthenticationModule
	{
		public AuthenticationScheme Scheme { get; } = AuthenticationScheme.Negotiate;
		public string AuthenticationType { get; } = "Negotiate";
		public bool CanPreAuthenticate { get; } = true;

		public Authorization? Authenticate (string challenge, HttpURLConnection request, ICredentials credentials)
			=> AuthChallengeResponseHelper.Authenticate (AuthenticationType, challenge, request, credentials);

		public Authorization? PreAuthenticate (HttpURLConnection request, ICredentials credentials)
			=> AuthChallengeResponseHelper.Authenticate (AuthenticationType, challenge, request, credentials);
	}
}
