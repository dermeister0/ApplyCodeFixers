using System.Collections.Generic;

namespace AbbreviationFix
{
    public class Config
    {
        public static Config Instance { get; set; }

        public HashSet<string> AbbreviationsToSkip { get; set; }
    }
}
