﻿// ***********************************************************************
// Copyright (c) 2014 Charlie Poole
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// ***********************************************************************

using System;
using System.Globalization;
using System.Xml;
using NUnit.Common.ColorConsole;

namespace NUnit.ConsoleRunner
{
    using Options;
    using Utilities;

    public class ResultReporter
    {
        private ExtendedTextWriter _writer;
        private XmlNode _result;
        private string _overallResult;
        private ConsoleOptions _options;

        private int _reportIndex = 0;

        public ResultReporter(XmlNode result, ExtendedTextWriter writer, ConsoleOptions options)
        {
            _result = result;
            _writer = writer;

            _overallResult = result.GetAttribute("result");
            if (_overallResult == "Skipped")
                _overallResult = "Warning";

            _options = options;

            Summary = new ResultSummary(result);
        }

        public ResultSummary Summary { get; private set; }

        /// <summary>
        /// Reports the results to the console
        /// </summary>
        public void ReportResults()
        {
            _writer.WriteLine();

            if (_options.StopOnError && Summary.FailureCount + Summary.ErrorCount > 0)
            {
                _writer.WriteLine(ColorStyle.Failure, "Execution terminated after first error");
                _writer.WriteLine();
            }

            WriteSummaryReport();

            WriteAssemblyErrorsAndWarnings();

            if (_overallResult == "Failed")
                WriteErrorsAndFailuresReport();

            if (Summary.SkipCount + Summary.IgnoreCount > 0)
                WriteNotRunReport();
        }

        #region Summary Report

        public void WriteSummaryReport()
        {
            ColorStyle overall = _overallResult == "Passed"
                ? ColorStyle.Pass
                : _overallResult == "Failed" 
                    ? ColorStyle.Failure
                    : _overallResult == "Warning"
                        ? ColorStyle.Warning
                        : ColorStyle.Output;
            
            _writer.WriteLine(ColorStyle.SectionHeader, "Test Run Summary");
            _writer.WriteLabelLine("    Overall result: ", _overallResult, overall);

            _writer.WriteLabel("   Tests run: ", Summary.RunCount.ToString(CultureInfo.CurrentUICulture));
            _writer.WriteLabel(", Passed: ", Summary.PassCount.ToString(CultureInfo.CurrentCulture));
            _writer.WriteLabel(", Errors: ", Summary.ErrorCount.ToString(CultureInfo.CurrentUICulture));
            _writer.WriteLabel(", Failures: ", Summary.FailureCount.ToString(CultureInfo.CurrentUICulture));
            _writer.WriteLabelLine(", Inconclusive: ", Summary.InconclusiveCount.ToString(CultureInfo.CurrentUICulture));

            var notRunTotal = Summary.SkipCount + Summary.IgnoreCount + Summary.InvalidCount;
            _writer.WriteLabel("     Not run: ", notRunTotal.ToString(CultureInfo.CurrentUICulture));
            _writer.WriteLabel(", Invalid: ", Summary.InvalidCount.ToString(CultureInfo.CurrentUICulture));
            _writer.WriteLabel(", Ignored: ", Summary.IgnoreCount.ToString(CultureInfo.CurrentUICulture));
            _writer.WriteLabelLine(", Skipped: ", Summary.SkipCount.ToString(CultureInfo.CurrentUICulture));

            var duration = _result.GetAttribute("duration", 0.0);
            var startTime = _result.GetAttribute("start-time", DateTime.MinValue);
            var endTime = _result.GetAttribute("end-time", DateTime.MaxValue);

            _writer.WriteLabelLine("  Start time: ", startTime.ToString("u"));
            _writer.WriteLabelLine("    End time: ", endTime.ToString("u"));
            _writer.WriteLabelLine("    Duration: ", string.Format("{0} seconds", duration.ToString("0.000")));
            _writer.WriteLine();
        }

        #endregion

        #region Assembly Errors and Warnings

        public void WriteAssemblyErrorsAndWarnings()
        {
            foreach (XmlNode node in _result.SelectNodes("descendant::test-suite[@type='Assembly']"))
            {
                if (node.GetAttribute("runstate") == "NotRunnable")
                    WriteAssemblyMessage(ColorStyle.Error, node.SelectSingleNode("properties/property[@name='_SKIPREASON']").GetAttribute("value"));
                else if (node.GetAttribute("total") == "0" || node.GetAttribute("testcasecount") == "0")
                    WriteAssemblyMessage(ColorStyle.Warning, "Warning: No tests found in " + node.GetAttribute("name"));
            }
        }

        private void WriteAssemblyMessage(ColorStyle style, string message)
        {
            _writer.WriteLine(style, message);
            _writer.WriteLine();
        }

        #endregion

        #region Errors and Failures Report

        public void WriteErrorsAndFailuresReport()
        {
            _reportIndex = 0;
            _writer.WriteLine(ColorStyle.SectionHeader, "Errors and Failures");
            _writer.WriteLine();
            WriteErrorsAndFailures(_result);
        }

        private void WriteErrorsAndFailures(XmlNode result)
        {
            string resultState = result.GetAttribute("result");

            switch (result.Name)
            {
                case "test-case":
                    if (resultState == "Failed")
                    {
                        using (new ColorConsole(ColorStyle.Failure))
                            WriteSingleResult(result);
                    }
                    return;

                case "test-run":
                    foreach (XmlNode childResult in result.ChildNodes)
                        WriteErrorsAndFailures(childResult);
                    break;

                case "test-suite":
                    if (resultState == "Failed")
                    {
                        if (result.GetAttribute("type") == "Theory")
                        {
                            using (new ColorConsole(ColorStyle.Failure))
                                WriteSingleResult(result);
                        }
                        else
                        {
                            var site = result.GetAttribute("site");
                            if (site == "SetUp" || site == "TearDown")
                                using (new ColorConsole(ColorStyle.Failure))
                                    WriteSingleResult(result);
                            if (site == "SetUp") return;
                        }
                    }
                    
                    foreach (XmlNode childResult in result.ChildNodes)
                        WriteErrorsAndFailures(childResult);

                    break;
            }
        }

        #endregion

        #region Not Run Report

        public void WriteNotRunReport()
        {
            _reportIndex = 0;
            _writer.WriteLine(ColorStyle.SectionHeader, "Tests Not Run");
            _writer.WriteLine();
            WriteNotRunResults(_result);
        }

        private void WriteNotRunResults(XmlNode result)
        {
            switch (result.Name)
            {
                case "test-case":
                    string status = result.GetAttribute("result");

                    if (status == "Skipped")
                    {
                        string label = result.GetAttribute("label");

                        var colorStyle = label == "Ignored" 
                            ? ColorStyle.Warning 
                            : ColorStyle.Output;

                        using (new ColorConsole(colorStyle))
                            WriteSingleResult(result);
                    }

                    break;

                case "test-suite":
                case "test-run":
                    foreach (XmlNode childResult in result.ChildNodes)
                        WriteNotRunResults(childResult);

                    break;
            }
        }

        #endregion

        #region Helper Methods

        private static readonly char[] EOL_CHARS = new char[] { '\r', '\n' };

        private void WriteSingleResult(XmlNode result)
        {
            string status = result.GetAttribute("label");
            if (status == null)
                status = result.GetAttribute("result");

            if (status == "Failed" || status == "Error")
            {
                var site = result.GetAttribute("site");
                if (site == "SetUp" || site == "TearDown")
                    status = site + " " + status;
            }

            string fullName = result.GetAttribute("fullname");

            _writer.WriteLine("{0}) {1} : {2}", ++_reportIndex, status, fullName);

            XmlNode failureNode = result.SelectSingleNode("failure");
            if (failureNode != null)
            {
                XmlNode message = failureNode.SelectSingleNode("message");
                XmlNode stacktrace = failureNode.SelectSingleNode("stack-trace");

                // In order to control the format, we trim any line-end chars
                // from end of the strings we write and supply them via calls
                // to WriteLine(). Newlines within the strings are retained.

                if (message != null)
                    _writer.WriteLine(message.InnerText.TrimEnd(EOL_CHARS));

                if (stacktrace != null)
                    _writer.WriteLine(stacktrace.InnerText.TrimEnd(EOL_CHARS));
            }

            XmlNode reasonNode = result.SelectSingleNode("reason");
            if (reasonNode != null)
            {
                XmlNode message = reasonNode.SelectSingleNode("message");

                if (message != null)
                    _writer.WriteLine(message.InnerText.TrimEnd(EOL_CHARS));
            }

            _writer.WriteLine(); // Skip after each item
        }

        #endregion
    }
}
