using System;
using System.Collections;
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
	// TODO is the name too long and weird?
	public sealed class NTAuthenticationAndroidMessageHandler : HttpMessageHandler
	{
		private readonly AndroidMessageHandler _handler;

		public NTAuthenticationAndroidMessageHandler (AndroidMessageHandler handler)
		{
			_handler = handler ?? throw new ArgumentNullException(nameof(handler));
		}

		// idea: this could be just an extension method :thinking:
		protected override async Task <HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var response = await _handler.SendAsyncInternal (request, cancellationToken);

			if (TryGetSupportedAuthMethod (request.RequestUri, out var auth, out var credentials)) {
				var authContext = InitializeAuthContext (auth.Scheme, credentials);
				var preAuthenticate = _handler.PreAuthenticate;
				var preAuthenticationData = _handler.PreAuthenticationData;

				try {
					response = await SendWithAuthAsync (request, auth, authContext, cancellationToken);
				} finally {
					_handler.PreAuthenticate = preAuthenticate;
					_handler.PreAuthenticationData = preAuthenticationData;
					authContext.CloseContext ();
				}
			}

			return response;
		}

		private async Task <HttpResponseMessage> SendWithAuthAsync (HttpRequestMessage request, AuthenticationData auth, NTAuthentication authContext, CancellationToken cancellationToken)
		{
			string authType = auth.ToString ();
			string? challenge = null;
			while (true) {
				if (auth.UseProxyAuthentication) {
					request.Headers.ProxyAuthorization = new AuthenticationHeaderValue (authType, authContext.GetOutgoingBlob (challenge));
				} else {
					request.Headers.Authorization = new AuthenticationHeaderValue (authType, authContext.GetOutgoingBlob (challenge));
				}

				var response = await _handler.SendAsyncInternal (request, cancellationToken);

				// if the server closes the connection we need to start again
				// TODO is this necessary or just give up?
				if (response.Headers.ConnectionClose.GetValueOrDefault ()) {
					challenge = null;
					continue;
				}

				// TODO proxy auth?
				var authenticationHeaderValues = auth.UseProxyAuthentication ? response.Headers.ProxyAuthenticate : response.Headers.WwwAuthenticate;
				challenge = authenticationHeaderValues?.FirstOrDefault (headerValue => headerValue.Scheme == authType)?.Parameter;

				if (response.StatusCode != HttpStatusCode.Unauthorized || authContext.IsCompleted || challenge is null) {
					return response;
				}

				// we need to drain the content otherwise the next request
				// won't reuse the same TCP socket and persistent auth won't work
				// TODO max buffer size?
				// await response.Content.LoadIntoBufferAsync ().WaitAsync (cancellationToken);
				await response.Content.LoadIntoBufferAsync ();
			}
		}

		private bool TryGetSupportedAuthMethod (
			Uri uri,
			[NotNullWhen (true)] out AuthenticationData? supportedAuth,
			[NotNullWhen (true)] out NetworkCredential? suitableCredentials)
		{
			IEnumerable <AuthenticationData> requestedAuthentication = _handler.RequestedAuthentication ?? Enumerable.Empty <AuthenticationData> ();
			foreach (var auth in requestedAuthentication) {
				if (auth.Scheme == AuthenticationScheme.Ntlm || auth.Scheme == AuthenticationScheme.Negotiate) {
					var authType = auth.Scheme.ToString ();
					var credentials = auth.UseProxyAuthentication ? _handler.Proxy?.Credentials : _handler.Credentials;
					suitableCredentials = credentials.GetCredential (uri, authType) as NetworkCredential;

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

		private static NTAuthentication InitializeAuthContext (AuthenticationScheme authType, NetworkCredential credentials)
		{
			var authContext = new NTAuthentication (
				isServer: false,
				authType.ToString (),
				credentials,

				// TODO
				spn: null,
				requestedContextFlags: 0,
				channelBinding: null
			);

			return authContext;
		}
	}
}
