﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.


using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DWScripter
{
    class Program
    {
        static void Main(string[] args)
        {
            string server = "";
            string sourceDb = "";
            string userName = "";
            string pwd = "";
            string wrkMode = "ALL";
            string filterSpec = "%";
            string outFile = "";
            string system = "PDW";
            string authentication = "SQL";
            string mode = "";
            string ExcludeObjectSuffixList = " "; //ex: "_old|_new|_test|_dba"; " " matches nothing by default
            string SchemaFilter = " "; // single schema filter
            string serverTarget = "";
            //string strportTarget = "";
            string TargetDb = "";
            string userNameTarget = "";
            string pwdTarget = "";
            string featureToScript = "";
            string FiltersFilePath = "";
            string CommandTimeout = "";

            Dictionary<String, String> parameters = new Dictionary<string, string>();
            parameters = GetParametersFromArguments(args);

            foreach (string pKey in parameters.Keys)
            {
                switch (pKey)
                {
                    case "-S":
                        server = parameters[pKey];
                        break;
                    case "-D":
                        sourceDb = parameters[pKey];
                        break;
                    case "-E":
                        authentication = "WINDOWS";
                        break;
                    case "-U":
                        userName = parameters[pKey];
                        break;
                    case "-P":
                        pwd = parameters[pKey];
                        break;
                    case "-W":
                        wrkMode = parameters[pKey];
                        break;
                    case "-M":
                        mode = parameters[pKey];
                        break;
                    case "-St":
                        serverTarget = parameters[pKey];
                        break;
                    case "-Dt":
                        TargetDb = parameters[pKey];
                        break;
                    case "-Ut":
                        userNameTarget = parameters[pKey];
                        break;
                    case "-Pt":
                        pwdTarget = parameters[pKey];
                        break;
                    case "-O":
                        outFile = parameters[pKey];
                        break;
                    case "-F":
                        featureToScript = parameters[pKey].ToUpper();
                        break;
                    case "-Fp":
                        FiltersFilePath = parameters[pKey];
                        break;
                    case "-X":
                        ExcludeObjectSuffixList = parameters[pKey];
                        break;
                    case "-C":
                        SchemaFilter = "^" + parameters[pKey] + "\\.";
                        break;
                    case "-t":
                        CommandTimeout = parameters[pKey];
                        break;
                    default:
                        break;
                }
            }

            if (wrkMode != "ALL" & wrkMode != "DDL" & wrkMode != "DML")
            {
                Console.WriteLine("Uknown mode. USE: DML|DDL|ALL");
                return;
            }

            if (
                mode == "Compare"
                & (String.IsNullOrEmpty(serverTarget) || String.IsNullOrEmpty(TargetDb))
            )
            {
                Console.WriteLine("Target Database elements must be completed ...");
                return;
            }

            PDWscripter c = null;
            PDWscripter cTarget = null;
            Boolean SourceFromFile = false;
            try
            {
                if (
                    mode == "Full"
                    || mode == "Delta"
                    || mode == "Compare"
                    || mode == "PersistStructure"
                )
                {
                    c = new PDWscripter(
                        system,
                        server,
                        sourceDb,
                        authentication,
                        userName,
                        pwd,
                        wrkMode,
                        ExcludeObjectSuffixList,
                        SchemaFilter,
                        filterSpec,
                        mode,
                        CommandTimeout
                    );
                    if (mode == "PersistStructure")
                        // populate dbstruct class
                        c.getDbstructure(outFile, wrkMode, true);
                    if (mode == "Compare")
                        c.getDbstructure(outFile, wrkMode, false);
                }
                else
                    c = new PDWscripter();

                // generate full database script
                if (mode == "Full" || mode == "Delta")
                {
                    c.getDbTables(false);
                    c.IterateScriptAllTables(c, outFile);
                }
                if (mode == "Compare" || mode == "CompareFromFile")
                {
                    SourceFromFile = false;

                    if (wrkMode == "ALL" || wrkMode == "DDL")
                    {
                        if (mode == "CompareFromFile")
                        {
                            // retrieve database structure from JSON DDL file
                            SourceFromFile = true;
                            // intialize from Json file
                            outFile = outFile.Replace(TargetDb, sourceDb);
                            string outDBJsonStructureFile = outFile + "_STRUCT_DDL.json";
                            c.GetDDLstructureFromJSONfile(outDBJsonStructureFile);
                        }
                        else
                            c.getDbTables(false);
                    }

                    if (mode == "CompareFromFile")
                    {
                        if (wrkMode == "ALL" || wrkMode == "DML")
                        {
                            // retrieve database structure from JSON DML file
                            SourceFromFile = true;
                            // intialize from Json file
                            outFile = outFile.Replace(TargetDb, sourceDb);
                            string outDBJsonStructureFile = outFile + "_STRUCT_DML.json";
                            c.GetDMLstructureFromJSONfile(outDBJsonStructureFile);
                        }
                    }

                    FilterSettings Filters = new FilterSettings();
                    if (featureToScript != "ALL")
                    {
                        // retrieve filter settings from file
                        Console.WriteLine(
                            "Retrieving filter settings file : "
                                + FiltersFilePath
                                + "- Feature : "
                                + featureToScript
                                + " - Database : ..."
                        );
                        GlobalFilterSettings gFilter = new GlobalFilterSettings();
                        Filters = gFilter.GetObjectsFromFile(
                            FiltersFilePath,
                            featureToScript,
                            sourceDb
                        );

                        if (Filters == null)
                        {
                            throw new System.ArgumentException(
                                "Filter settings parameter can not be null - initialization from file : "
                                    + FiltersFilePath
                                    + "- Feature : "
                                    + featureToScript
                                    + " - Database : ..."
                            );
                        }

                        Console.WriteLine("Filter settings OK");
                    }

                    cTarget = new PDWscripter(
                        system,
                        serverTarget,
                        TargetDb,
                        authentication,
                        userNameTarget,
                        pwdTarget,
                        wrkMode,
                        "%",
                        "%",
                        filterSpec,
                        mode,
                        CommandTimeout
                    );
                    Console.WriteLine("Target Connection Opened");
                    cTarget.getDbstructure(outFile, wrkMode, false);
                    if (mode != "CompareFromFile")
                        cTarget.getDbTables(false);

                    cTarget.CompIterateScriptAllTables(
                        c,
                        cTarget,
                        outFile,
                        SourceFromFile,
                        Filters
                    );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw ex;
            }

            if (c.conn != null)
            {
                c.conn.Close();
            }

            if (cTarget != null)
            {
                cTarget.conn.Close();
            }

            Console.Write("Done !!! ");
            Environment.Exit(0);
        }

        public static void DisplayHelp()
        {
            Console.WriteLine("DWScripter Command Line Tool");
            Console.WriteLine("Usage DWScripter ");
            Console.WriteLine("     [-S: Server source]");
            Console.WriteLine("     [-D: Database Source]");
            Console.WriteLine("     [-E: Trusted connection]");
            Console.WriteLine("     [-U: Login source id]");
            Console.WriteLine("     [-P: Password source]");
            Console.WriteLine("     [-W: work mode [DML|DDL|ALL]");
            Console.WriteLine(@"     [-O: Work folder path \ suffix file ]  no space allowed");
            Console.WriteLine("     [-M: mode [Full|PersistStructure|Compare|CompareFromFile]]");
            Console.WriteLine("     [-St: Server target]");
            Console.WriteLine("     [-dt: Database Target]");
            Console.WriteLine("     [-Ut: login id target]");
            Console.WriteLine("     [-Pt: password target]");
            Console.WriteLine("     [-F: filter on feature for scripting]");
            Console.WriteLine("     [-Fp: filters file path] no space allowed");
            Console.WriteLine("     [-C: Schema Filter");
            Console.WriteLine("     [-X: Exclusion Filter");
            Console.WriteLine("     [-t: Command Timeout]");
            Console.WriteLine();
            Console.WriteLine(
                @"Sample : DWScripter -S:workspace.sql.azuresynapse.net,17001 -D:dbname -E -O:DB_Name -M:PersistStructure"
            );
            Console.WriteLine(
                @"Sample : DWScripter -St:workspace.sql.azuresynapse.net -Dt:targetdb -E -O:DEV -M:CompareFromFile -F:ALL"
            );
            Console.WriteLine(
                @"Sample : DWScripter -St:workspace.sql.azuresynapse.net -Dt:targetdb -E -O:DEV -M:CompareFromFile -F:SPRINT2 -Fp:GlobalDWFilterSettings.json -d:stagedb"
            );
            return;
        }

        static Dictionary<string, string> GetParametersFromArguments(string[] args)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();
            string ParametersList = "-S|-D|-E|-M|-O|-St|-Dt|-U|-P|-Ut|-Pt|-W|-F|-Fp|-X|-C|-t";
            List<string> ParametersHelp = new List<string> { "-help", "-?", "/?" };
            List<string> ModeList = new List<string>
            {
                "FULL",
                "COMPARE",
                "COMPAREFROMFILE",
                "PERSISTSTRUCTURE"
            };
            Regex Plist = new Regex(ParametersList);
            string ParameterSwitch = "";
            string value = "";
            int SeparatorPosition;

            for (var x = 0; x < args.Count(); x++)
            {
                SeparatorPosition = args[x].IndexOf(":");
                if (SeparatorPosition != -1)
                {
                    ParameterSwitch = args[x].Substring(0, SeparatorPosition);
                    value = args[x].Substring(
                        SeparatorPosition + 1,
                        args[x].Length - SeparatorPosition - 1
                    );
                }
                else
                {
                    ParameterSwitch = args[x];
                    value = "";
                }

                if (ParametersHelp.Contains(ParameterSwitch))
                {
                    DisplayHelp();
                    Environment.Exit(0);
                }

                if (Plist.IsMatch(ParameterSwitch))
                {
                    parameters.Add(ParameterSwitch, value);
                }
                else
                {
                    Console.WriteLine(String.Format("Invalid switch: {0}", ParameterSwitch));
                    Environment.Exit(1);
                }
            }

            if (parameters.ContainsKey("-M"))
            {
                if (!ModeList.Contains(parameters["-M"].ToUpper()))
                {
                    Console.WriteLine(
                        "Value "
                            + parameters["-M"]
                            + "is not allowed. Only values FULL, COMPARE, COMPAREFROMFILE, PERSISTSTRUCTURE for parameter -M"
                    );
                    Environment.Exit(1);
                }
            }
            else
            {
                Console.WriteLine("Argument -M mode missing");
                Environment.Exit(1);
            }

            // check feature switch existence when work mode different from PERSISTSTRUCTURE or FULL mode
            if (
                parameters["-M"].ToUpper() != "PERSISTSTRUCTURE"
                && parameters["-M"].ToUpper() != "FULL"
            )
            {
                if (!parameters.ContainsKey("-F"))
                {
                    Console.WriteLine("Argument -F is missing, fill it to continue");
                    Environment.Exit(1);
                }
                else
                {
                    if (!parameters.ContainsKey("-D") && parameters["-F"].ToUpper() != "ALL")
                    {
                        Console.WriteLine(
                            "Argument -D is missing [Database Name], fill it to continue"
                        );
                        Environment.Exit(1);
                    }

                    if (!parameters.ContainsKey("-Fp") && parameters["-F"].ToUpper() != "ALL")
                    {
                        Console.WriteLine(
                            "Argument -Fp is missing [Filter file], fill it to continue"
                        );
                        Environment.Exit(1);
                    }
                }
            }
            return parameters;
        }
    }
}
