
using System.Web;

namespace NetworkMonitor.Utils;

public class EncryptionHelper{

    public static string EncryptStr(string emailEncryptKey,string str)
        {
            str = AesOperation.EncryptString(emailEncryptKey, str);
            return HttpUtility.UrlEncode(str);
        }
}