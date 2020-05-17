using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Xml.Linq;
using NUnit.Engine;
using NUnit.Engine.Extensibility;

namespace Tesults.NUnit
{
    [Extension(EngineVersion = "3.4")]
    public class TesultsEventListener : ITestEventListener
    {
        string target = null;
        string config = null;
        string files = null;
        string buildName = null;
        string buildDesc = null;
        string buildResult = null;
        string buildReason = null;
        bool disabled = false;
        bool paramDataExtracted = false;

        List<Dictionary<string, object>> cases = new List<Dictionary<string, object>>();

        private List<string> FilesForTest(string suite, string name)
        {
            var filePaths = new List<string>();
            if (files == null)
            {
                return filePaths;
            }
            string dir = null;
            try
            {
                dir = Path.Combine(@files, suite, name);
            }
            catch (ArgumentException)
            {
                Console.WriteLine("tesults-files has invalid characters in path");
                return filePaths;
            }
            try
            {
                var files = Directory.GetFiles(dir);
                foreach (var file in files)
                {
                    filePaths.Add(file);
                }
            }
            catch (Exception)
            {
                // Test may not have files
            }
            return filePaths;
        }

        private string ConfigValueForKey (string key, Configuration configuration)
        {
            if (configuration == null || key == null)
            {
                return null;
            }

            var kvp = configuration.AppSettings.Settings[key];
            if (kvp == null)
            {
                return null;
            } else
            {
                try
                {
                    return kvp.Value;
                }
                catch (Exception)
                {
                    // Swallow
                    return null;
                }
            }
        }

        public void OnTestEvent(string report)
        {
            if (disabled)
            {
                return;
            }
            var doc = XDocument.Parse(report);
            if (doc.Root.Name == "test-suite" && paramDataExtracted == false)
            {
                var settings = doc.Root.Element("settings");
                foreach (var setting in settings.Elements())
                {
                    if (setting.Attribute("name").Value.Equals("TestParametersDictionary"))
                    {
                        var testParams = setting.Elements();
                        foreach (var param in testParams)
                        {
                            if (param.Attribute("key").Value == "tesults-target")
                            {
                                paramDataExtracted = true; // Only need to do this once per test run
                                target = param.Attribute("value").Value;
                            }
                            if (param.Attribute("key").Value == "tesults-config")
                            {
                                config = param.Attribute("value").Value;
                            }
                            if (param.Attribute("key").Value == "tesults-files")
                            {
                                files = param.Attribute("value").Value;
                            }
                            if (param.Attribute("key").Value == "tesults-build-name")
                            {
                                buildName = param.Attribute("value").Value;
                            }
                            if (param.Attribute("key").Value == "tesults-build-desc")
                            {
                                buildDesc = param.Attribute("value").Value;
                            }
                            if (param.Attribute("key").Value == "tesults-build-result")
                            {
                                buildResult = param.Attribute("value").Value;
                            }
                            if (param.Attribute("key").Value == "tesults-build-reason")
                            {
                                buildReason = param.Attribute("value").Value;
                            }
                        }
                    }
                }

                if (target == null)
                {
                    disabled = true;
                    Console.WriteLine("Tesults disabled. No tesults-target param supplied.");
                }
            }

            if (doc.Root.Name == "test-case")
            {
                var suite = doc.Root.Attribute("classname").Value;
                suite = suite.Substring(suite.LastIndexOf('.') + 1);

                var testCase = new Dictionary<string, object>();
                testCase.Add("name", doc.Root.Attribute("name").Value);
                testCase.Add("suite", suite);
                testCase.Add("_Start Time", doc.Root.Attribute("start-time").Value);
                testCase.Add("_End Time", doc.Root.Attribute("end-time").Value);
                testCase.Add("_Duration", doc.Root.Attribute("duration").Value);

                var resultRaw = doc.Root.Attribute("result").Value;
                var result = "unknown";
                if (resultRaw.Equals("Passed"))
                {
                    result = "pass";
                }
                else if (resultRaw.Equals("Failed"))
                {
                    result = "fail";
                    try
                    {
                        var failure = doc.Root.Element("failure");
                        //var output = doc.Root.Element("output");
                        //var assertions = doc.Root.Element("assertions");

                        var message = failure.Element("message").Value;
                        var stacktrace = failure.Element("stack-trace").Value;

                        testCase.Add("reason", message);
                        testCase.Add("_Stack Trace", stacktrace);
                    }
                    catch (Exception)
                    {
                        // Continue if issues extracting reason
                    }
                }
                testCase.Add("result", result);

                try
                {
                    var properties = doc.Root.Element("properties");
                    var index = 1;
                    foreach (var property in properties.Elements())
                    {
                        if (property.Attribute("name").Value.ToLower().Equals("description"))
                        {
                            testCase.Add("desc", property.Attribute("value").Value);
                        }
                        else if (property.Attribute("name").Value.ToLower().Equals("author"))
                        {
                            testCase.Add("_Author " + index, property.Attribute("value").Value);
                        }
                        else
                        {
                            testCase.Add("_" + property.Attribute("name").Value, property.Attribute("value").Value);
                        }
                        index++;
                    }
                }
                catch (Exception)
                {
                    // Continue if issues extracting properties
                }

                cases.Add(testCase);
            }

            if (doc.Root.Name == "test-run") // end of test run
            {
                if (config != null)
                {
                    var fileMap = new ExeConfigurationFileMap();
                    fileMap.ExeConfigFilename = config; // Path to config file
                    Configuration configuration = null;
                    try
                    {
                        configuration = ConfigurationManager.OpenMappedExeConfiguration(fileMap, ConfigurationUserLevel.None); // OpenMappedMachineConfiguration(fileMap);
                    }
                    catch (Exception)
                    {
                        // Swallow
                    }

                    if (configuration != null)
                    {
                        var configTarget = ConfigValueForKey(target, configuration);
                        if (configTarget != null)
                        {
                            target = configTarget;
                        }
                        if (files == null)
                        {
                            files = ConfigValueForKey("tesults-files", configuration);
                        }
                        if (buildName == null)
                        {
                            buildName = ConfigValueForKey("tesults-build-name", configuration);
                        }
                        if (buildDesc == null)
                        {
                            buildDesc = ConfigValueForKey("tesults-build-desc", configuration);
                        }
                        if (buildResult == null)
                        {
                            buildResult = ConfigValueForKey("tesults-build-result", configuration);
                        }
                        if (buildReason == null)
                        {
                            buildReason = ConfigValueForKey("tesults-build-reason", configuration);
                        }
                    }
                }

                foreach (var testCase in cases)
                {
                    var filesForCase = FilesForTest(testCase["suite"].ToString(), testCase["name"].ToString());
                    if (filesForCase.Count > 0)
                    {
                        testCase.Add("files", filesForCase);
                    }
                }

                if (buildName != null)
                {
                    var buildCase = new Dictionary<string, object>();
                    buildCase.Add("name", buildName);
                    if (buildResult != null)
                    {
                        if (buildResult.ToLower() == "pass")
                        {
                            buildCase.Add("result", "pass");
                        }
                        else if (buildResult.ToLower() == "fail")
                        {
                            buildCase.Add("result", "fail");
                        }
                        else
                        {
                            buildCase.Add("result", "unknown");
                        }
                    }
                    else
                    {
                        buildCase.Add("result", "unknown");
                    }
                    if (buildDesc != null)
                    {
                        buildCase.Add("desc", buildDesc);
                    }
                    if (buildReason != null)
                    {
                        buildCase.Add("reason", buildReason);
                    }
                    buildCase.Add("suite", "[build]");
                    var filesForCase = FilesForTest(buildCase["suite"].ToString(), buildCase["name"].ToString());
                    if (filesForCase.Count > 0)
                    {
                        buildCase.Add("files", filesForCase);
                    }
                    cases.Add(buildCase);
                }

                var results = new Dictionary<string, object>();
                results.Add("cases", cases);

                var data = new Dictionary<string, object>();
                data.Add("target", target);
                data.Add("results", results);

                // Upload.
                Console.WriteLine("Tesults results upload...");
                var response = Tesults.Results.Upload(data);
                Console.WriteLine("Success: " + response["success"]);
                Console.WriteLine("Message: " + response["message"]);
                Console.WriteLine("Warnings: " + ((List<string>)response["warnings"]).Count);
                Console.WriteLine("Errors: " + ((List<string>)response["errors"]).Count);
            }
        }
    }
}
