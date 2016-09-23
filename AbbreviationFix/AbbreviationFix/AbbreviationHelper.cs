using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using StyleCop.Analyzers.Settings.ObjectModel;

namespace AbbreviationFix
{
    internal class AbbreviationHelper
    {
        public const int MaxRenameCount = int.MaxValue;

        private static int renameCount;

        private static Dictionary<string, string> renameList = new Dictionary<string, string>
        {
            /*["FAREVP"] = "FarEvp",
            ["EVPs"] = "Evps",
            ["ATTUID"] = "AttUId",
            ["CSSPK"] = "CssPK",
            ["EVPSSA"] = "EvpSsa",
            ["EVPDHS"] = "EvpDhs",
            ["I9PDFTD"] = "I9PdfTD",
            ["IDPK"] = "IdPK",
            ["WSID"] = "WSId",
            ["APID"] = "APId",
            ["ALLBU"] = "AllBU",
            ["CSCEVP"] = "CscEvp",
            ["CSCFI9"] = "CscFI9",
            ["FARI9EVP"] = "FarI9Evp"*/
            /*["dOB"] = "dob",
            ["sSN"] = "ssn",
            ["eVP"] = "evp",
            ["hTML"] = "html"*/
            /*["WSCSCID"] = "WSCscId",
            ["DMVDHS"] = "DmvDhc"*/
            ["ClientCompanyWSID"] = "ClientCompanyWSId",
            ["ClientCompanyWsid"] = "ClientCompanyWSId",
            ["BatchIDPK"] = "BatchIdPK",
            ["BatchIdpk"] = "BatchIdPK",
            ["WMID"] = "WMId",
            ["Wmid"] = "WMId",
            ["HOSTIPAddress"] = "HostIPAddress",
            ["HostipAddress"] = "HostIPAddress"
        };

        /// <summary>
        /// Match opts (usefull for test):
        /// public static int NAME +
        /// public static int NameDDisable3DD +
        /// public static int Name3DDaDDaDD ++
        /// public static int Name3DS1+
        /// public static int NameDX3+
        /// public static int DX3name +
        /// public static int D3Xcase; -
        /// public static int Name773DB33TFTname222DXS +++
        /// public static int Name33nA -
        /// </summary>
        /// <param name="syntaxToken"></param>
        /// <param name="settings"></param>
        /// <returns></returns>
        public static IEnumerable<Match> GetAbbreviationsInSymbol(SyntaxToken syntaxToken, AbbreviationSettings settings)
        {
            var regex = @"\d+[A-Z]{2,}$|\d+[A-Z]{3,}|[A-Z]{2,}$|[A-Z]{2,}\d+|[A-Z]{3,}";

            Func<SyntaxToken, bool> isInterface =
                token => token.Parent is InterfaceDeclarationSyntax && syntaxToken.ValueText[0] == 'I';
            if (isInterface(syntaxToken))
            {
                // Ignore I symbol for interfaces. ReSharper abbreviation fixing logic does not consider 'I'. E.g. IDDeal is not abbreviation.
                regex = $@"^I|({regex})";
            }

            var matches = Regex.Matches(syntaxToken.ValueText, regex);

            foreach (Match match in matches)
            {
                if (isInterface(syntaxToken) &&
                    match == matches[0])
                {
                    // [UGLY]: Ignore first match. Will be happy if someone will rewrite regex to avoid these tricks in code
                    continue;
                }

                var length = match.Index + match.Value.Length;
                if (syntaxToken.ValueText.Length > length)
                {
                    length++;
                }

                // Check registred abbreviations. Handle them in ReSharper way. If word continues after abbreviation
                // last capital letter is not included to abbreviation - it is start of new word.
                var onlySymbols =
                    Regex.Match(
                        syntaxToken.ValueText.Substring(match.Index, length - match.Index),
                        @"([A-Z]{2,})(?![a-z])");

                if (settings.AbbreviationsToSkip.Contains(onlySymbols.Value))
                {
                    continue;
                }

                yield return match;
            }
        }

        public static string RenameAll(string oldIdentifier)
        {
            string newIdentifier = oldIdentifier;

            foreach (var pair in renameList)
            {
                newIdentifier = newIdentifier.Replace(pair.Key, pair.Value);
            }

            return newIdentifier;
        }

        public static void CheckElementNameToken(SyntaxNodeAnalysisContext context, SyntaxToken identifier, AbbreviationSettings settings, DiagnosticDescriptor descriptor)
        {
            string oldIdentifier = identifier.ValueText;
            string newIdentifier = RenameAll(oldIdentifier);

            if (oldIdentifier != newIdentifier)
            {
                context.ReportDiagnostic(Diagnostic.Create(descriptor, identifier.GetLocation(), identifier.ValueText));
            }
        }
    }
}
