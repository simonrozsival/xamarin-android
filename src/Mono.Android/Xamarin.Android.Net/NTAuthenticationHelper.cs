using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Xamarin.Android.Net
{
	internal static class NTAuthenticationHelper
	{
		private const int MaxRequests = 10;

		internal static bool TryGetSupportedAuthMethod (
			AndroidMessageHandler handler,
			HttpRequestMessage request,
			[NotNullWhen (true)] out AuthenticationData? supportedAuth,
			[NotNullWhen (true)] out NetworkCredential? suitableCredentials)
		{
			IEnumerable<AuthenticationData> requestedAuthentication = handler.RequestedAuthentication ?? Enumerable.Empty<AuthenticationData> ();
			foreach (var auth in requestedAuthentication) {
				if (TryGetSupportedAuthType (auth.Challenge, out var authType)) {
					var credentials = auth.UseProxyAuthentication ? handler.Proxy?.Credentials : handler.Credentials;
					suitableCredentials = credentials?.GetCredential (request.RequestUri, authType);

					if (suitableCredentials != null) {
						supportedAuth = auth;
						return true;
					}
				}
			}

			supportedAuth = null;
			suitableCredentials = null;
			return false;
		}

		internal static async Task <HttpResponseMessage> SendAsync (
			AndroidMessageHandler handler,
			HttpRequestMessage request,
			AuthenticationData auth,
			NetworkCredential credentials,
			CancellationToken cancellationToken)
		{
			var authType = GetSupportedAuthType (auth.Challenge);
			var authContext = InitializeAuthContext (authType, credentials);

			// we need to make sure that the handler doesn't override the authorization header
			// with the user defined pre-authentication data
			var originalPreAuthenticate = handler.PreAuthenticate;
			handler.PreAuthenticate = false;

			try {
				string? challenge = null;
				int requestCounter = 0;

				while (true) {
					var headerValue = new AuthenticationHeaderValue (authType, authContext.GetOutgoingBlob (challenge));
					if (auth.UseProxyAuthentication) {
						request.Headers.ProxyAuthorization = headerValue;
					} else {
						request.Headers.Authorization = headerValue;
					}

					var response = await handler.DoSendAsync (request, cancellationToken);

					// we need to drain the content otherwise the next request
					// won't reuse the same TCP socket and persistent auth won't work
					// TODO can I avoid this somehow and achieve the same result?
					await response.Content.LoadIntoBufferAsync ();

					if (++requestCounter >= MaxRequests) {
						return response;
					}

					// if the server closes the connection we need to start over again
					if (response.Headers.ConnectionClose.GetValueOrDefault ()) {
						challenge = null;
						continue;
					}

					var responseHeaderValues = auth.UseProxyAuthentication ? response.Headers.ProxyAuthenticate : response.Headers.WwwAuthenticate;
					challenge = responseHeaderValues?.FirstOrDefault (headerValue => headerValue.Scheme == authType)?.Parameter;

					if (response.StatusCode != HttpStatusCode.Unauthorized || authContext.IsCompleted || challenge is null) {
						return response;
					}
				}
			} finally {
				handler.PreAuthenticate = originalPreAuthenticate;
				authContext.CloseContext ();
			}
		}

		private static NTAuthentication InitializeAuthContext (string authType, NetworkCredential credentials)
		{
			var authContext = new NTAuthentication (
				isServer: false,
				authType,
				credentials,
				spn: null, // TODO
				requestedContextFlags: 0, // TODO
				channelBinding: null // TODO
			);

			return authContext;
		}

		private static string GetSupportedAuthType (string challenge) {
			if (!TryGetSupportedAuthType (challenge, out var authType)) {
				throw new InvalidOperationException ($"Authenticaton scheme {authType} is not supported by NTAuthenticationHelper.");
			}

			return authType;
		}

		private static bool TryGetSupportedAuthType (string challenge, out string authType) {
			var spaceIndex = challenge.IndexOf (' ');
			authType = spaceIndex == -1 ? challenge : challenge.Substring (0, spaceIndex);

			return authType.Equals ("NTLM", StringComparison.OrdinalIgnoreCase) ||
				authType.Equals ("Negotiate", StringComparison.OrdinalIgnoreCase);
		}
	}
}
