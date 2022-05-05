using System;
using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Xamarin.Android.Net.TEMPORARY
{
	// References:
	// a. Usenet 1994 - RC4 Algorithm revealed
	// http://www.qrst.de/html/dsds/rc4.htm
	internal sealed class RC4 : IDisposable
	{
		private byte[]? state;
		private byte x;
		private byte y;

		public RC4(ReadOnlySpan<byte> key)
		{
			state = ArrayPool<byte>.Shared.Rent(256);

			byte index1 = 0;
			byte index2 = 0;

			for (int counter = 0; counter < 256; counter++)
			{
				state[counter] = (byte)counter;
			}

			for (int counter = 0; counter < 256; counter++)
			{
				index2 = (byte)(key[index1] + state[counter] + index2);
				(state[counter], state[index2]) = (state[index2], state[counter]);
				index1 = (byte)((index1 + 1) % key.Length);
			}
		}

		public void Dispose()
		{
			if (state != null)
			{
				x = 0;
				y = 0;
				CryptographicOperations.ZeroMemory(state.AsSpan(0, 256));
				ArrayPool<byte>.Shared.Return(state);
				state = null;
			}
		}

		public void Transform(ReadOnlySpan<byte> input, Span<byte> output)
		{
			Debug.Assert(input.Length == output.Length);
			Debug.Assert(state != null);

			for (int counter = 0; counter < input.Length; counter++)
			{
				x = (byte)(x + 1);
				y = (byte)(state[x] + y);
				(state[x], state[y]) = (state[y], state[x]);
				byte xorIndex = (byte)(state[x] + state[y]);
				output[counter] = (byte)(input[counter] ^ state[xorIndex]);
			}
		}
	}
}
