﻿// Copyright (c) Rackspace, US Inc. All Rights Reserved. Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace Rackspace.Net
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using DictionaryEntry = System.Collections.DictionaryEntry;
    using IDictionary = System.Collections.IDictionary;
    using IEnumerable = System.Collections.IEnumerable;

    /// <summary>
    /// Represents a URI Template expression of the form <c>{x,y}</c> or <c>{+x,y}</c>.
    /// </summary>
    internal sealed class UriTemplatePartSimpleExpansion : UriTemplatePartExpansion
    {
        /// <summary>
        /// <see langword="true"/> to escape reserved characters during rendering; otherwise, <see langword="false"/>.
        /// </summary>
        private readonly bool _escapeReserved;

        /// <summary>
        /// Initializes a new instance of the <see cref="UriTemplatePartSimpleExpansion"/> class.
        /// </summary>
        /// <param name="variables">A collection of variables to expand for this expression.</param>
        /// <param name="escapeReserved"><see langword="true"/> to escape reserved characters during rendering; otherwise, <see langword="false"/>.</param>
        /// <exception cref="ArgumentNullException">If <paramref name="variables"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// If <paramref name="variables"/> is empty.
        /// <para>-or-</para>
        /// <para>If <paramref name="variables"/> contains any <see langword="null"/> values.</para>
        /// </exception>
        public UriTemplatePartSimpleExpansion(IEnumerable<VariableReference> variables, bool escapeReserved)
            : base(variables)
        {
            _escapeReserved = escapeReserved;
        }

        /// <inheritdoc/>
        /// <value>
        /// <see cref="UriTemplatePartType.SimpleStringExpansion"/> for templates of the form <c>{x,y}</c>.
        /// <para>-or-</para>
        /// <para><see cref="UriTemplatePartType.ReservedStringExpansion"/> for templates of the form <c>{+x,y}</c>.</para>
        /// </value>
        public override UriTemplatePartType Type
        {
            get
            {
                return _escapeReserved ? UriTemplatePartType.SimpleStringExpansion : UriTemplatePartType.ReservedStringExpansion;
            }
        }

        /// <inheritdoc/>
        protected override void BuildPatternBodyImpl(StringBuilder pattern, ICollection<string> listVariables, ICollection<string> mapVariables)
        {
            if (pattern == null)
                throw new ArgumentNullException("pattern");
            if (listVariables == null)
                throw new ArgumentNullException("listVariables");
            if (mapVariables == null)
                throw new ArgumentNullException("mapVariables");

            List<string> variablePatterns = new List<string>();
            foreach (var variable in Variables)
            {
                bool allowReservedSet = Type == UriTemplatePartType.ReservedStringExpansion;
                variablePatterns.Add(BuildVariablePattern(variable, allowReservedSet, null, listVariables, mapVariables));
            }

            pattern.Append("(?:");
            AppendOneOrMoreToEnd(pattern, variablePatterns, 0);
            pattern.Append(")?");
        }

        private static string BuildVariablePattern(VariableReference variable, bool allowReservedSet, string groupName, ICollection<string> listVariables, ICollection<string> mapVariables)
        {
            string characterPattern;
            if (allowReservedSet)
                characterPattern = "(?:" + UnreservedCharacterPattern + "|" + ReservedCharacterPattern + ")";
            else
                characterPattern = "(?:" + UnreservedCharacterPattern + ")";

            string valueStartPattern;
            if (!string.IsNullOrEmpty(groupName))
                valueStartPattern = "(?<" + groupName + ">";
            else
                valueStartPattern = "(?:";

            string valueEndPattern = ")";

            string keyStartPattern;
            if (!string.IsNullOrEmpty(groupName))
                keyStartPattern = "(?<" + groupName + "key>";
            else
                keyStartPattern = "(?:";

            string keyEndPattern = ")";

            string mapValueStartPattern;
            if (!string.IsNullOrEmpty(groupName))
                mapValueStartPattern = "(?<" + groupName + "value>";
            else
                mapValueStartPattern = "(?:";

            string mapValueEndPattern = ")";

            string countPattern;
            if (allowReservedSet)
                countPattern = "*?";
            else
                countPattern = "*";

            StringBuilder variablePattern = new StringBuilder();

            if (variable.Prefix != null)
            {
                // by this point we know to match the variable as a simple string
                variablePattern.Append(valueStartPattern);
                variablePattern.Append(characterPattern);
                variablePattern.Append("{0,").Append(variable.Prefix).Append("}");
                variablePattern.Append(valueEndPattern);
                return variablePattern.ToString();
            }

            bool treatAsList = listVariables.Contains(variable.Name);
            bool treatAsMap = mapVariables.Contains(variable.Name);

            variablePattern.Append("(?:");

            if (!variable.Composite && !treatAsList && !treatAsMap)
            {
                // could be a simple string
                variablePattern.Append(valueStartPattern);
                variablePattern.Append(characterPattern).Append(countPattern);
                variablePattern.Append(valueEndPattern);
                variablePattern.Append("|");
            }

            if (treatAsList || !treatAsMap)
            {
                // could be an associative array
                variablePattern.Append(valueStartPattern).Append(characterPattern).Append(countPattern).Append(valueEndPattern);
                variablePattern.Append("(?:,");
                variablePattern.Append(valueStartPattern).Append(characterPattern).Append(countPattern).Append(valueEndPattern);
                variablePattern.Append(")*?");
            }

            if (treatAsMap || !treatAsList)
            {
                if (!treatAsMap)
                    variablePattern.Append('|');

                // could be an associative map
                char separator = variable.Composite ? '=' : ',';
                variablePattern.Append(valueStartPattern);
                variablePattern.Append(keyStartPattern);
                variablePattern.Append(characterPattern).Append(countPattern);
                variablePattern.Append(keyEndPattern);
                variablePattern.Append(separator).Append(mapValueStartPattern).Append(characterPattern).Append(countPattern).Append(mapValueEndPattern);
                variablePattern.Append(valueEndPattern);
                variablePattern.Append("(?:,");
                variablePattern.Append(valueStartPattern);
                variablePattern.Append(keyStartPattern);
                variablePattern.Append(characterPattern).Append(countPattern);
                variablePattern.Append(keyEndPattern);
                variablePattern.Append(separator).Append(mapValueStartPattern).Append(characterPattern).Append(countPattern).Append(mapValueEndPattern);
                variablePattern.Append(valueEndPattern);
                variablePattern.Append(")*?");
            }

            variablePattern.Append(")");

            return variablePattern.ToString();
        }

        private static void AppendOneOrMoreToEnd(StringBuilder pattern, List<string> patterns, int startIndex)
        {
            if (startIndex >= patterns.Count)
                throw new ArgumentException();

            pattern.Append("(?:");

            if (startIndex < patterns.Count - 1)
            {
                // include the first item and at least one more from there to the end
                pattern.Append(patterns[startIndex]).Append(",");
                AppendOneOrMoreToEnd(pattern, patterns, startIndex + 1);
                pattern.Append("|");
            }

            // include the first item alone
            pattern.Append(patterns[startIndex]);

            if (startIndex < patterns.Count - 1)
            {
                // don't include the first item, but do include one or more to the end
                pattern.Append("|");
                AppendOneOrMoreToEnd(pattern, patterns, startIndex + 1);
            }

            pattern.Append(")");
        }

        protected internal override KeyValuePair<VariableReference, object>[] Match(string text, ICollection<string> listVariables, ICollection<string> mapVariables)
        {
            List<string> variablePatterns = new List<string>();
            for (int i = 0; i < Variables.Count; i++)
            {
                bool allowReservedSet = Type == UriTemplatePartType.ReservedStringExpansion;
                variablePatterns.Add(BuildVariablePattern(Variables[i], allowReservedSet, "var" + i, listVariables, mapVariables));
            }

            StringBuilder matchPattern = new StringBuilder();
            matchPattern.Append("^");
            AppendOneOrMoreToEnd(matchPattern, variablePatterns, 0);
            matchPattern.Append("$");

            Regex matchExpression = new Regex(matchPattern.ToString());
            Match match = matchExpression.Match(text);

            List<KeyValuePair<VariableReference, object>> results = new List<KeyValuePair<VariableReference, object>>();
            for (int i = 0; i < Variables.Count; i++)
            {
                Group group = match.Groups["var" + i];
                if (!group.Success || group.Captures.Count == 0)
                    continue;

                if (Variables[i].Prefix != null)
                {
                    if (group.Success && group.Captures.Count == 1)
                    {
                        results.Add(new KeyValuePair<VariableReference, object>(Variables[i], DecodeCharacters(group.Captures[0].Value)));
                    }

                    continue;
                }

                bool treatAsList = listVariables.Contains(Variables[i].Name);
                bool treatAsMap = mapVariables.Contains(Variables[i].Name);

                bool considerString = !Variables[i].Composite && !treatAsList && !treatAsMap;
                bool considerList = treatAsList || !treatAsMap;
                bool considerMap = treatAsMap || !treatAsList;

                // first check for a map
                Group mapKeys = match.Groups["var" + i + "key"];
                if (mapKeys.Success && mapKeys.Captures.Count > 0)
                {
                    Debug.Assert(considerMap);
                    Group mapValues = match.Groups["var" + i + "value"];
                    Dictionary<string, string> map = new Dictionary<string, string>();
                    for (int j = 0; j < mapKeys.Captures.Count; j++)
                        map.Add(DecodeCharacters(mapKeys.Captures[j].Value), DecodeCharacters(mapValues.Captures[j].Value));

                    results.Add(new KeyValuePair<VariableReference, object>(Variables[i], map));
                    continue;
                }

                // next try a list
                if (!considerString || group.Captures.Count > 1)
                {
                    Debug.Assert(considerList);
                    List<string> list = new List<string>(group.Captures.Cast<Capture>().Select(capture => DecodeCharacters(capture.Value)));
                    results.Add(new KeyValuePair<VariableReference, object>(Variables[i], list));
                    continue;
                }

                Debug.Assert(considerString);
                results.Add(new KeyValuePair<VariableReference, object>(Variables[i], DecodeCharacters(group.Captures[0].Value)));
            }

            return results.ToArray();
        }

        protected override void RenderElement(StringBuilder builder, VariableReference variable, object variableValue, bool first)
        {
            if (builder == null)
                throw new ArgumentNullException("builder");
            if (variableValue == null)
                throw new ArgumentNullException("variableValue");

            if (!first)
                builder.Append(',');

            AppendText(builder, variable, variableValue.ToString(), _escapeReserved);
        }

        protected override void RenderEnumerable(StringBuilder builder, VariableReference variable, IEnumerable variableValue, bool first)
        {
            foreach (object value in variableValue)
            {
                if (value == null)
                    continue;

                RenderElement(builder, variable, value, first);
                first = false;
            }
        }

        protected override void RenderDictionary(StringBuilder builder, VariableReference variable, IDictionary variableValue, bool first)
        {
            foreach (DictionaryEntry entry in variableValue)
            {
                if (variable.Composite)
                {
                    if (!first)
                        builder.Append(',');

                    AppendText(builder, variable, entry.Key.ToString(), _escapeReserved);
                    builder.Append('=');
                    AppendText(builder, variable, entry.Value.ToString(), _escapeReserved);
                }
                else
                {
                    RenderElement(builder, variable, entry.Key, first);
                    RenderElement(builder, variable, entry.Value, false);
                }

                first = false;
            }
        }

        public override string ToString()
        {
            return string.Format("{{{0}{1}}}", Type == UriTemplatePartType.SimpleStringExpansion ? string.Empty : "+", string.Join(",", Variables.Select(i => i.Name).ToArray()));
        }
    }
}
