using System.Text;

namespace DeEarthX.Infrastructure.TextEncoding;

public static class EncodingInitializer
{
    public const string GbkEncodingName = "GBK";
    private static int _initialized;

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch
        {
        }
    }

    public static Encoding GetGbk()
    {
        Initialize();
        return Encoding.GetEncoding(GbkEncodingName);
    }

    public static bool TryGetEncoding(string name, out Encoding encoding)
    {
        Initialize();
        try
        {
            encoding = Encoding.GetEncoding(name);
            return true;
        }
        catch
        {
            encoding = Encoding.Default;
            return false;
        }
    }
}
