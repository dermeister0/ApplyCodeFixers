using System;
using System.IO;

namespace StyleCopTester
{
    internal class ConfigLoader
    {
        public static void Load(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return;
            
            if (!File.Exists(fileName))
                throw new Exception("Config file does not exist.");
            
            var config = Newtonsoft.Json.JsonConvert.DeserializeObject<StyleCopConfig>(File.ReadAllText(fileName));
            AbbreviationFix.Config.Instance = config.AbbreviationFix;
        }
    }
}
