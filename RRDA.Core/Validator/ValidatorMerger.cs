using System.Xml.Linq;

namespace RRDA.Core.Validator
{
    /// <summary>
    /// Utility class for merging validator XML files.
    /// Merges a new validator with an existing one, preserving existing Field and Map elements
    /// while adding only new elements that don't have a matching definedName.
    /// </summary>
    public static class ValidatorMerger
    {
        /// <summary>
        /// Merges a new validator XML with an existing one.
        /// The merged validator contains all elements from the original validator,
        /// plus any elements from the new validator that do not have a corresponding
        /// match in the original based on the 'definedName' attribute.
        /// Manually entered aliases in Map tags are preserved.
        /// </summary>
        /// <param name="existingValidatorPath">Path to the existing validator file.</param>
        /// <param name="newValidatorPath">Path to the newly generated validator file.</param>
        /// <param name="outputPath">Path where the merged validator will be saved.</param>
        /// <exception cref="ArgumentNullException">Thrown if any parameter is null or whitespace.</exception>
        /// <exception cref="InvalidDataException">Thrown if either XML file is invalid.</exception>
        public static void MergeValidators(
            string existingValidatorPath,
            string newValidatorPath,
            string outputPath)
        {
            if (string.IsNullOrWhiteSpace(existingValidatorPath))
                throw new ArgumentNullException(nameof(existingValidatorPath));
            if (string.IsNullOrWhiteSpace(newValidatorPath))
                throw new ArgumentNullException(nameof(newValidatorPath));
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentNullException(nameof(outputPath));

            XDocument existingDoc;
            XDocument newDoc;

            try
            {
                existingDoc = XDocument.Load(existingValidatorPath);
                newDoc = XDocument.Load(newValidatorPath);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    $"Impossibile caricare i file di validazione per l'unione: {ex.Message}",
                    ex);
            }

            var existingRoot = existingDoc.Root
                ?? throw new InvalidDataException("Validatore esistente privo di elemento root.");
            var newRoot = newDoc.Root
                ?? throw new InvalidDataException("Nuovo validatore privo di elemento root.");

            // Start with the existing validator as the base
            var mergedRoot = new XElement(existingRoot);

            // Merge Cells (Cell elements)
            MergeCells(mergedRoot, newRoot);

            // Merge FieldMappings (Map elements)
            MergeFieldMappings(mergedRoot, newRoot);

            // Merge FieldRules (Field elements)
            MergeFieldRules(mergedRoot, newRoot);

            // I campi calcolati sono definiti manualmente e non devono essere persi
            // durante la rigenerazione del validatore.
            MergeCalculatedFields(mergedRoot, newRoot);

            // Merge Sheets (optional, but preserve if any exist in new validator)
            MergeSheets(mergedRoot, newRoot);

            // Save the merged document
            try
            {
                var mergedDoc = new XDocument(
                    new XDeclaration("1.0", "utf-8", "yes"),
                    mergedRoot);
                mergedDoc.Save(outputPath);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    $"Impossibile salvare il validatore unito: {ex.Message}",
                    ex);
            }
        }

        /// <summary>
        /// Merges FieldMappings (Map elements) from new validator into existing.
        /// Preserves existing mappings and their manually entered aliases.
        /// Adds only new Map elements that don't have a matching definedName.
        /// </summary>
        private static void MergeFieldMappings(XElement mergedRoot, XElement newRoot)
        {
            var existingMappings = mergedRoot.Element("FieldMappings");
            var newMappings = newRoot.Element("FieldMappings");

            if (newMappings == null || !newMappings.Elements("Map").Any())
                return;

            if (existingMappings == null)
            {
                existingMappings = new XElement("FieldMappings");
                // Insert after Sheets or at the beginning if no Sheets
                var sheetsElement = mergedRoot.Element("Sheets");
                if (sheetsElement != null)
                    sheetsElement.AddAfterSelf(existingMappings);
                else
                    mergedRoot.AddFirst(existingMappings);
            }

            // Get all existing definedNames (case-insensitive)
            var existingDefinedNames = existingMappings
                .Elements("Map")
                .Select(m => (string?)m.Attribute("definedName") ?? string.Empty)
                .Where(dn => !string.IsNullOrWhiteSpace(dn))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Add new Map elements that don't already exist
            foreach (var newMap in newMappings.Elements("Map"))
            {
                var newDefinedName = (string?)newMap.Attribute("definedName") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(newDefinedName))
                    continue;

                if (!existingDefinedNames.Contains(newDefinedName))
                {
                    existingMappings.Add(new XElement(newMap));
                }
            }
        }

        /// <summary>
        /// Merges FieldRules (Field elements) from new validator into existing.
        /// Adds only new Field elements that don't have a matching definedName.
        /// </summary>
        private static void MergeFieldRules(XElement mergedRoot, XElement newRoot)
        {
            var existingFieldRules = mergedRoot.Element("FieldRules");
            var newFieldRules = newRoot.Element("FieldRules");

            if (newFieldRules == null || !newFieldRules.Elements("Field").Any())
                return;

            if (existingFieldRules == null)
            {
                existingFieldRules = new XElement("FieldRules");
                // Insert after FieldMappings or Sheets
                var insertAfter = mergedRoot.Element("FieldMappings")
                    ?? mergedRoot.Element("Sheets");

                if (insertAfter != null)
                    insertAfter.AddAfterSelf(existingFieldRules);
                else
                    mergedRoot.Add(existingFieldRules);
            }

            // Get all existing definedNames (case-insensitive)
            var existingDefinedNames = existingFieldRules
                .Elements("Field")
                .Select(f => (string?)f.Attribute("definedName") ?? string.Empty)
                .Where(dn => !string.IsNullOrWhiteSpace(dn))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Add new Field elements that don't already exist
            foreach (var newField in newFieldRules.Elements("Field"))
            {
                var newDefinedName = (string?)newField.Attribute("definedName") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(newDefinedName))
                    continue;

                if (!existingDefinedNames.Contains(newDefinedName))
                {
                    existingFieldRules.Add(new XElement(newField));
                }
            }
        }

        private static void MergeCalculatedFields(XElement mergedRoot, XElement newRoot)
        {
            var incoming = newRoot.Element("CalculatedFields");
            if (incoming is null || !incoming.Elements("Field").Any())
                return;

            var existing = mergedRoot.Element("CalculatedFields");
            if (existing is null)
            {
                existing = new XElement("CalculatedFields");
                var fieldRules = mergedRoot.Element("FieldRules");
                if (fieldRules is not null)
                    fieldRules.AddAfterSelf(existing);
                else
                    mergedRoot.Add(existing);
            }

            var aliases = existing.Elements("Field")
                .Select(field => (string?)field.Attribute("alias"))
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var field in incoming.Elements("Field"))
            {
                var alias = (string?)field.Attribute("alias");
                if (!string.IsNullOrWhiteSpace(alias) && aliases.Add(alias))
                    existing.Add(new XElement(field));
            }
        }

        /// <summary>
        /// Merges Sheets from new validator into existing (if not already present).
        /// </summary>
        private static void MergeSheets(XElement mergedRoot, XElement newRoot)
        {
            var newSheets = newRoot.Element("Sheets");

            if (newSheets == null || !newSheets.Elements("Sheet").Any())
                return;

            var existingSheets = mergedRoot.Element("Sheets");

            if (existingSheets == null)
            {
                // Add Sheets at the beginning (after root attributes)
                mergedRoot.AddFirst(new XElement(newSheets));
                return;
            }

            // Get all existing sheet names (case-insensitive)
            var existingSheetNames = existingSheets
                .Elements("Sheet")
                .Select(s => (string?)s.Attribute("Name") ?? string.Empty)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Add new Sheet elements that don't already exist
            foreach (var newSheet in newSheets.Elements("Sheet"))
            {
                var newSheetName = (string?)newSheet.Attribute("Name") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(newSheetName))
                    continue;

                if (!existingSheetNames.Contains(newSheetName))
                {
                    existingSheets.Add(new XElement(newSheet));
                }
            }
        }

        /// <summary>
        /// Merges Cells from new validator into existing (if not already present).
        /// </summary>
        private static void MergeCells(XElement mergedRoot, XElement newRoot)
        {
            var newCells = newRoot.Element("Cells");

            if (newCells == null || !newCells.Elements("Cell").Any())
                return;

            var existingCells = mergedRoot.Element("Cells");

            if (existingCells == null)
            {
                existingCells = new XElement("Cells");
                // Insert after Sheets or at the beginning if no Sheets
                var sheetsElement = mergedRoot.Element("Sheets");
                if (sheetsElement != null)
                    sheetsElement.AddAfterSelf(existingCells);
                else
                    mergedRoot.AddFirst(existingCells);
            }

            // Get all existing coordinate+definedNameAlias pairs (case-insensitive)
            var existingCellKeys = existingCells
                .Elements("Cell")
                .Select(c => (string?)c.Attribute("coordinate") ?? string.Empty)
                .Zip(
                    existingCells.Elements("Cell")
                        .Select(c => (string?)c.Attribute("definedNameAlias") ?? string.Empty),
                    (coord, alias) => $"{coord}|{alias}")
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Add new Cell elements that don't already exist
            foreach (var newCell in newCells.Elements("Cell"))
            {
                var newCoord = (string?)newCell.Attribute("coordinate") ?? string.Empty;
                var newAlias = (string?)newCell.Attribute("definedNameAlias") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(newCoord) || string.IsNullOrWhiteSpace(newAlias))
                    continue;

                var key = $"{newCoord}|{newAlias}";
                if (!existingCellKeys.Contains(key))
                {
                    existingCells.Add(new XElement(newCell));
                }
            }
        }

    }
}
