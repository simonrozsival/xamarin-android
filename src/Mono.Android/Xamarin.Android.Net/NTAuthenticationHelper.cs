using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication.ExtendedProtection;
using System.Threading;
using System.Threading.Tasks;

namespace Xamarin.Android.Net
{
	// This code is heavily inspired by System.Net.Http.AuthenticationHelper
	internal static class NTAuthenticationHelper
	{
		const int MaxRequests = 10;

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
			HttpResponseMessage response,
			AuthenticationData auth,
			NetworkCredential credentials,
			CancellationToken cancellationToken)
		{
			var authType = GetSupportedAuthType (auth.Challenge);
			var isProxyAuth = auth.UseProxyAuthentication;
			var authContext = await InitializeAuthContext (handler, request, authType, isProxyAuth, credentials, cancellationToken).ConfigureAwait (false);

			// we need to make sure that the handler doesn't override the authorization header
			// with the user defined pre-authentication data
			var originalPreAuthenticate = handler.PreAuthenticate;
			handler.PreAuthenticate = false;

			try {
				string? challenge = null;
				int requestCounter = 0;

				while (requestCounter++ < MaxRequests) {
					var challengeResponse = authContext.GetOutgoingBlob (challenge);
					if (challengeResponse == null) {
						// response indicated denial even after login, so stop processing
						// and return current response
						break;
					}

					var headerValue = new AuthenticationHeaderValue (authType, challengeResponse);
					if (auth.UseProxyAuthentication) {
						request.Headers.ProxyAuthorization = headerValue;
					} else {
						request.Headers.Authorization = headerValue;
					}

					response = await handler.DoSendAsync (request, cancellationToken).ConfigureAwait (false);

					// we need to drain the content otherwise the next request
					// won't reuse the same TCP socket and persistent auth won't work
					await Drain (response).ConfigureAwait (false);

					if (authContext.IsCompleted || !TryGetChallenge (response, authType, isProxyAuth, out challenge)) {
						break;
					}

					if (!IsAuthenticationChallenge (response, isProxyAuth)) {
						// Tail response for Negotiate on successful authentication.
						// Validate it before we proceed.
						authContext.GetOutgoingBlob (challenge);
						break;
					}
				}

				return response;
			} finally {
				handler.PreAuthenticate = originalPreAuthenticate;
				authContext.CloseContext ();
			}
		}

		static string GetSupportedAuthType (string challenge)
		{
			if (!TryGetSupportedAuthType (challenge, out var authType)) {
				throw new InvalidOperationException ($"Authenticaton scheme {authType} is not supported by {nameof (NTAuthenticationHelper)}.");
			}

			return authType;
		}

		static bool TryGetSupportedAuthType (string challenge, out string authType)
		{
			var spaceIndex = challenge.IndexOf (' ');
			authType = spaceIndex == -1 ? challenge : challenge.Substring (0, spaceIndex);

			return authType.Equals ("NTLM", StringComparison.OrdinalIgnoreCase) ||
				authType.Equals ("Negotiate", StringComparison.OrdinalIgnoreCase);
		}

		static async Task<NTAuthentication> InitializeAuthContext (
			AndroidMessageHandler handler,
			HttpRequestMessage request,
			string authType,
			bool isProxyAuth,
			NetworkCredential credentials,
			CancellationToken cancellationToken)
		{
			var spn = await GetSpn (handler, request, isProxyAuth, cancellationToken);
			var flags = GetRequestedContextFlags (isProxyAuth);
			return new NTAuthenticationProxy (isServer: false, authType, credentials, spn, flags, channelBinding: null);
		}

		static async Task<string?> GetSpn (
			AndroidMessageHandler handler,
			HttpRequestMessage request,
			bool isProxyAuth,
			CancellationToken cancellationToken)
		{
			// Calculate SPN (Service Principal Name) using the host name of the request.
			// Use the request's 'Host' header if available. Otherwise, use the request uri.
			// Ignore the 'Host' header if this is proxy authentication since we need to use
			// the host name of the proxy itself for SPN calculation.
			string hostName;
			if (!isProxyAuth && request.Headers.Host != null)
			{
				// Use the host name without any normalization.
				hostName = request.Headers.Host;
			}
			else
			{
				// TODO if I understand it correctly then the authUri is either the Proxy URI or the destination URI...
				// and I need to verify that this is the correct way to get it...
				var authUri = isProxyAuth
					? handler.Proxy?.GetProxy (request.RequestUri) ?? request.RequestUri!
					: request.RequestUri!;

				// Need to use FQDN normalized host so that CNAME's are traversed.
				// Use DNS to do the forward lookup to an A (host) record.
				// But skip DNS lookup on IP literals. Otherwise, we would end up
				// doing an unintended reverse DNS lookup.
				if (authUri.HostNameType == UriHostNameType.IPv6 || authUri.HostNameType == UriHostNameType.IPv4) {
					hostName = authUri.IdnHost;
				} else {
					IPHostEntry result = await Dns.GetHostEntryAsync (authUri.IdnHost, cancellationToken).ConfigureAwait (false);
					hostName = result.HostName;
				}

				if (!isProxyAuth && !authUri.IsDefaultPort && UsePortInSpn) {
					hostName = string.Create (null, stackalloc char[128], $"{hostName}:{authUri.Port}");
				}
			}

			return $"HTTP/{hostName}";
		}

		static int GetRequestedContextFlags (bool isProxyAuth)
		{
			// the ContextFlagsPal from dotnet/runtime type is internal and we can't
			// use it directly here and we have to use ints directly
			int contextFlags = 0x00000800; // ContextFlagsPal.Connection

			// When connecting to proxy server don't enforce the integrity to avoid
			// compatibility issues. The assumption is that the proxy server comes
			// from a trusted source.
			if (!isProxyAuth)
			{
				contextFlags |= 0x00010000; // ContextFlagsPal.InitIntegrity
			}

			return contextFlags;
		}

		static Task Drain (HttpResponseMessage response)
			=> response.Content.LoadIntoBufferAsync ();

		static bool TryGetChallenge (
			HttpResponseMessage response,
			string authType,
			bool isProxyAuth,
			[NotNullWhen (true)] out string? challenge)
		{
			var responseHeaderValues = isProxyAuth ? response.Headers.ProxyAuthenticate : response.Headers.WwwAuthenticate;
			challenge = responseHeaderValues?.FirstOrDefault (headerValue => headerValue.Scheme == authType)?.Parameter;
			return !string.IsNullOrEmpty (challenge);
		}

		static bool IsAuthenticationChallenge (HttpResponseMessage response, bool isProxyAuth)
			=> isProxyAuth
				? response.StatusCode == HttpStatusCode.ProxyAuthenticationRequired
				: response.StatusCode == HttpStatusCode.Unauthorized;

		// TODO see AuthenticationHelper.NtAuth.cs
		static bool UsePortInSpn
			=> false;
	}
}
