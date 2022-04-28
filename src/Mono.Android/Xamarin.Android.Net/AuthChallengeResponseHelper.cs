using System;
using System.Net;
using System.Diagnostics.CodeAnalysis;

namespace Xamarin.Android.Net
{
	internal static class AuthChallengeResponseHelper
	{
		public static bool TryGetSupportedAuth(
			Uri uri,
			IEnumerable<AuthenticationData>? requestedAuthentication,
			[NotNullWhen(true)] out AuthenticationData? supportedAuth)
		{
			requestedAuthentication ??= Enumerable.Empty<AuthenticationData>();

			foreach (var auth in requestedAuthentication) {
				if (auth.Scheme != AuthenticationScheme.Ntlm && auth.Scheme != AuthenticationScheme.Negotiate)
					continue;

				var suitableCredentials = credentials.GetCredential (uri, auth.Scheme.ToLowerInvariant ());
				if (suitableCredentials == null)
					continue;

				supportedAuth = auth;
				return true;
			}

			supportedAuth = null;
			return false;
		}

		public static Authorization Authenticate (string authType, string challenge, HttpURLConnection request, ICredentials credentials)
		{
			if (credentials == null || string.IsNullOrEmpty (challenge) || !challenge.StartsWith (authType, StringComparison.OrdinalIgnoreCase))
				return null;

			NetworkCredential requestCredentials = credentials.GetCredential (new Uri (request.URL?.ToString ()!), authType.ToLowerInvariant ());
			if (requestCredentials == null)
				return null;

			challenge = challenge.Substring(authType.Length).Trim();

			// TODO does this have to be stateful?
			// TODO we actually need the NTAuthentication reflection proxy
			var authContext = new NTAuthenticationProxy(
				isServer: false,
				authType,
				requestCredentials,

				// TODO
				spn: null,
				requestedContextFlags: 0,
				channelBinding: null
			);

			return new Authorization ($"{authType} {authContext.GetOutgoingBlob(challenge)}");
		}
	}
}
