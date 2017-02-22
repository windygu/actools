﻿using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace AcTools.KnhFile {
    public partial class Knh {
        public string OriginalFilename { get; }

        private Knh([NotNull] KnhEntry entry) {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            OriginalFilename = string.Empty;
            RootEntry = entry;
        }

        private Knh(string filename, [NotNull] KnhEntry entry) {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            OriginalFilename = filename;
            RootEntry = entry;
        }

        [NotNull]
        public KnhEntry RootEntry;
    }
}
