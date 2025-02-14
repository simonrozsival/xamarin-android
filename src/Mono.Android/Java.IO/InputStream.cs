using Android.Runtime;

namespace Java.IO
{

#pragma warning disable RS0016
	public partial class InputStream
	{
		~InputStream ()
		{
			Logger.Log (LogLevel.Info, "Java.IO.InputStream", "Finalize");
		}

		protected override void Dispose (bool disposing)
		{
			Logger.Log (LogLevel.Info, "Java.IO.InputStream", new System.Diagnostics.StackTrace(true).ToString());
			base.Dispose(disposing);
		}
	}
}

