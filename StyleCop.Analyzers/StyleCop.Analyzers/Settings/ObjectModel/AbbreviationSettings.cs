// Copyright (c) Tunnel Vision Laboratories, LLC. All Rights Reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace StyleCop.Analyzers.Settings.ObjectModel
{
    [JsonObject(MemberSerialization.OptIn)]
    internal class AbbreviationSettings
    {
        /// <summary>
        /// This is the backing field for the <see cref="AbbreviationsToSkip"/> property.
        /// </summary>
        [JsonProperty("abbreviationsToSkip", DefaultValueHandling = DefaultValueHandling.Ignore)]
        private HashSet<string> abbreviationsToSkip;

        public HashSet<string> AbbreviationsToSkip => this.abbreviationsToSkip;
    }
}
