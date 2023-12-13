
using System.Web;

namespace NetworkMonitor.Utils
{

    public class EncryptionHelper
    {

        public static string EncryptStr(string emailEncryptKey, string str)
        {
            str = AesOperation.EncryptString(emailEncryptKey, str);
            return HttpUtility.UrlEncode(str);
        }
        public static bool IsBadKey(string emailEncryptKey, string encryptedStr, string checkStr)
        {
            string decryptString="";
            if (encryptedStr == "") return true;
            try
            {
                decryptString = AesOperation.DecryptString(emailEncryptKey, encryptedStr);
            }
            catch
            {
                return true;
            }
            return !decryptString.Equals(checkStr);
        }
    }
}