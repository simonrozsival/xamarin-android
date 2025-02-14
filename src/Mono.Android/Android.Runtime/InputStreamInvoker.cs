using System;
using System.IO;

namespace Android.Runtime
{
	public class InputStreamInvoker : Stream
	{
		public Java.IO.InputStream BaseInputStream {get; private set;}
		private IntPtr inputStreamGref;

		protected Java.Nio.Channels.FileChannel? BaseFileChannel {get; private set;}

		public InputStreamInvoker (Java.IO.InputStream stream)
		{
			if (stream == null)
				throw new ArgumentNullException (nameof (stream));

			BaseInputStream = stream;
			Log ("ctor");

			// // We need to keep a global reference to the Java.IO.InputStream instance
			// // so that it doesn't get garbage collected on the Java side while we're using it.
			// inputStreamGref = JNIEnv.NewGlobalRef (stream.Handle);

			Java.IO.FileInputStream? fileStream = stream as Java.IO.FileInputStream;
			if (fileStream != null)
				BaseFileChannel = fileStream.Channel;
		}

		//
		// Exception audit:
		//
		//  Verdict
		//    Exception wrapping is required.
		//
		//  Rationale
		//    `java.io.InputStream.close()` throws an exception, see:
		//
		//     https://developer.android.com/reference/java/io/InputStream?hl=en
		//
		protected override void Dispose (bool disposing)
		{
			Log ("Dispose");
			if (disposing && BaseInputStream != null) {
				try {
					BaseFileChannel = null;
					if (BaseInputStream.PeerReference.IsValid) {
						BaseInputStream.Close ();
					}
					BaseInputStream.Dispose ();

					if (inputStreamGref != IntPtr.Zero) {
						JNIEnv.DeleteGlobalRef (inputStreamGref);
						inputStreamGref = IntPtr.Zero;
					}
				} catch (Java.IO.IOException ex) when (JNIEnv.ShouldWrapJavaException (ex)) {
					throw new IOException (ex.Message, ex);
				}
			}
		}

		//
		// Exception audit:
		//
		//  Verdict
		//    Exception wrapping is required.
		//
		//  Rationale
		//    `java.io.InputStream.close()` throws an exception, see:
		//
		//     https://developer.android.com/reference/java/io/InputStream?hl=en
		//
		public override void Close ()
		{
			Log ("Close");
			base.Close ();
		}

		public override void Flush ()
		{
			// No need to flush an input stream
		}

		//
		// Exception audit:
		//
		//  Verdict
		//    Exception wrapping is required.
		//
		//  Rationale
		//    `java.io.InputStream.read(byte[], int, int)` throws an exception, see:
		//
		//     https://developer.android.com/reference/java/io/InputStream?hl=en#read(byte%5B%5D,%20int,%20int)
		//
		public override int Read (byte[] buffer, int offset, int count)
		{
			Log ("Read");
			int res;

			try {
				try {
					res = BaseInputStream.Read (buffer, offset, count);
				} catch (Java.IO.IOException ex) when (JNIEnv.ShouldWrapJavaException (ex)) {
					throw new IOException (ex.Message, ex);
				}
			} catch (Exception ex) {
				Logger.Log (LogLevel.Error, "Android.Runtime.InputStreamInvoker", $"Exception in Read: {ex}");
				throw;
			}

			if (res == -1) {
				Log ("Read res -1");
				return 0;
			}

			Log ($"Read res {res}");
			return res;
		}

		public override long Seek (long offset, SeekOrigin origin)
		{
			Log ("Seek");
			if (BaseFileChannel == null)
				throw new NotSupportedException ();

			switch (origin) {
			case SeekOrigin.Begin:
				BaseFileChannel.Position (offset);
				break;
			case SeekOrigin.Current:
				BaseFileChannel.Position (BaseFileChannel.Position() + offset);
				break;
			case SeekOrigin.End:
				BaseFileChannel.Position (BaseFileChannel.Size() + offset);
				break;
			}
			return BaseFileChannel.Position ();
		}

		public override void SetLength (long value)
		{
			throw new NotSupportedException ();
		}

		public override void Write (byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException ();
		}

		public override bool CanRead { get { return true; } }
		public override bool CanSeek { get { return (BaseFileChannel != null); } }
		public override bool CanWrite { get { return false; } }

		public override long Length {
			get {
				if (BaseFileChannel != null)
					return BaseFileChannel.Size ();
				else
					throw new NotSupportedException ();
			}
		}

		public override long Position {
			get {
				if (BaseFileChannel != null)
					return BaseFileChannel.Position ();
				else
					throw new NotSupportedException ();
			}
			set {
				if (BaseFileChannel != null)
					BaseFileChannel.Position (value);
				else
					throw new NotSupportedException ();
			}
		}

		public static Stream? FromJniHandle (IntPtr handle, JniHandleOwnership transfer)
		{
			Logger.Log (LogLevel.Debug, "Android.Runtime.InputStreamInvoker", $"FromJniHandle ({handle}, {transfer})");
			if (handle == IntPtr.Zero)
				return null;

			var inst = (IJavaObject?) Java.Lang.Object.PeekObject (handle);

			if (inst == null) {
				inst = (IJavaObject) Java.Interop.TypeManager.CreateInstance (handle, transfer);
				Log ("FromJniHandle: Created new instance", inst);
			} else {
				// JNIEnv.DeleteRef (handle, transfer);
				Log ("FromJniHandle: Reusing existing instance", inst);
			}

			return new InputStreamInvoker ((Java.IO.InputStream)inst);
		}

		private void Log (string msg)
		{
			Log (msg, BaseInputStream);
		}

		private static void Log (string msg, IJavaObject obj)
		{
			Logger.Log (LogLevel.Debug, "Android.Runtime.InputStreamInvoker", $"{msg} (type: {obj?.GetType()}, handle: 0x{obj?.Handle:x2}, identity hash: {(obj as global::Java.Lang.Object)?.JniIdentityHashCode}, peer reference is valid: {(obj as global::Java.Lang.Object)?.PeerReference.IsValid})");
			Logger.Log (LogLevel.Debug, "Android.Runtime.InputStreamInvoker", new System.Diagnostics.StackTrace(true).ToString());
		}
	}
}
