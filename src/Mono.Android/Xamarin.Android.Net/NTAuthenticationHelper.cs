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
		internal static bool TryGetSupportedAuthMethod (
			AndroidMessageHandler handler,
			HttpRequestMessage request,
			[NotNullWhen (true)] out AuthenticationData? supportedAuth,
			[NotNullWhen (true)] out NetworkCredential? suitableCredentials)
		{
			IEnumerable<AuthenticationData> requestedAuthentication = handler.RequestedAuthentication ?? Enumerable.Empty<AuthenticationData> ();
			foreach (var auth in requestedAuthentication) {
				if (TryGetNTAuthType (auth, out var authType)) {
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
			var authType = GetNTAuthType (auth);
			var authContext = InitializeAuthContext (authType, credentials);
			var originalPreAuthenticate = handler.PreAuthenticate;
			handler.PreAuthenticate = false;

			try {
				string? challenge = null;

				// TODO should there be a limit to how many loops are acceptable here to prevent infinite loop?
				while (true) {
					if (auth.UseProxyAuthentication) {
						request.Headers.ProxyAuthorization = new AuthenticationHeaderValue (authType, authContext.GetOutgoingBlob (challenge));
					} else {
						request.Headers.Authorization = new AuthenticationHeaderValue (authType, authContext.GetOutgoingBlob (challenge));
					}

					var response = await handler.DoSendAsync (request, cancellationToken);

					// if the server closes the connection we need to start again
					// TODO is this necessary or just give up?
					if (response.Headers.ConnectionClose.GetValueOrDefault ()) {
						challenge = null;
						continue;
					}

					var authenticationHeaderValues = auth.UseProxyAuthentication ? response.Headers.ProxyAuthenticate : response.Headers.WwwAuthenticate;
					challenge = authenticationHeaderValues?.FirstOrDefault (headerValue => headerValue.Scheme == authType)?.Parameter;

					if (response.StatusCode != HttpStatusCode.Unauthorized || authContext.IsCompleted || challenge is null) {
						return response;
					}

					// we need to drain the content otherwise the next request
					// won't reuse the same TCP socket and persistent auth won't work
					// TODO max buffer size?
					// TODO timeout?
					// TODO cancellation token? e.g., via .WaitAsync (cancellationToken);
					await response.Content.LoadIntoBufferAsync ();
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

				// TODO
				spn: null,
				requestedContextFlags: 0,
				channelBinding: null
			);

			return authContext;
		}

		private static string GetNTAuthType (AuthenticationData auth) {
			if (!TryGetNTAuthType (auth, out var authType)) {
				throw new InvalidOperationException ($"Authenticaton scheme {authType} is not supported.");
			}

			return authType;
		}

		private static bool TryGetNTAuthType (AuthenticationData auth, out string authType) {
			var spaceIndex = auth.Challenge.IndexOf(' ');
			authType = spaceIndex == -1 ? auth.Challenge : auth.Substring(0, spaceIndex);

			return authType.Equals("NTLM", StringComparison.OrdinalIgnoreCase) ||
				authType.Equals("Negotiate", StringComparison.OrdinalIgnoreCase));
		}
	}
}
