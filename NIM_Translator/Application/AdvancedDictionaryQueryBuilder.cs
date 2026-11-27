namespace NIM.ApplicationLayer
{
    internal static class AdvancedDictionaryQueryBuilder
    {
        private const int MaximumRowCount = 100000;

        internal static string Build(string sourceLangCode, string targetLangCode)
        {
            if (!string.IsNullOrEmpty(sourceLangCode) || !string.IsNullOrEmpty(targetLangCode))
            {
                return "Select * From AdvancedDictionary Limit " + MaximumRowCount;
            }

            return string.Format(
                "Select * From AdvancedDictionary Where [From] = '{0}' And [To] = '{1}' Limit {2}",
                sourceLangCode,
                targetLangCode,
                MaximumRowCount);
        }
    }
}
