﻿namespace Models.Core.Apsim710File
{
    using APSIM.Shared.OldAPSIM;
    using APSIM.Shared.Utilities;
    using Microsoft.CSharp;
    using Models.Core;
    using Models.Core.ApsimFile;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Xml;

    /// <summary>
    /// Manager script parameter
    /// </summary>
    public class ScriptParameter
    {
        /// <summary>
        /// The name of the extracted parameter
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// The value as a string
        /// </summary>
        public string Value { get; set; }
        /// <summary>
        /// The type name extracted from the original script
        /// </summary>
        public string TypeName { get; set; }
        /// <summary>
        /// The description for the parameter
        /// </summary>
        public string Description { get; set; }
        /// <summary>
        /// An enumeration
        /// </summary>
        public string ListValues { get; set; }
    }

    /// <summary>
    /// This is a worker class for the import process that converts
    /// an old APSIM 7.5 simulation file into an APSIM(X) file or Simulations object.
    /// <para/>
    /// Some of the components are added to the imported xml by creating instances of the
    /// Model object and then populating it then importing the serialized XML.
    /// Other components that have child components are done purely using XML creation
    /// and copying. This is because Model objects generate XML for their children and
    /// a merge would be required on the child XML. Easier just to do the
    /// whole component purely in XML (I hope).
    /// </summary>
    public class Importer
    {
        private string[] cropNames = {"AgPasture", "Bambatsi", "Banana2", "Barley", "BarleySWIM",
                                      "Broccoli","ButterflyPea","Canola","CanolaSWIM","Centro",
                                      "Chickpea","Chickpea2","Chicory","cloverseed","Cotton2",
                                      "Cowpea","EGrandis","EMelliodora","EPopulnea","Fababean",
                                      "Fieldpea","Fieldpea2","Soybean","FrenchBean","Grassseed","Heliotrope",
                                      "Horsegram","Itallianryegrass","Kale","Lablab","Lentil",
                                      "Lettuce","Lolium_rigidum","Lucerne","Lucerne2","LucerneSWIM",
                                      "Lupin","Maize","MaizeZ","Millet","Mucuna","Mungbean","Navybean",
                                      "Oats","Oryza","Oryza2","Ozcot","Peanut","Pigeonpea","Potato",
                                      "raphanus_raphanistrum","Root","Seedling","Slurp","Sorghum",
                                      "Soybean","Stylo","Sugar","Sugarcane","Sunflower","SweetCorn",
                                      "SweetSorghum","Taro2","Triticale","vine","Weed","WF_Millet",
                                      "Wheat","Wheat2","Wheat2X"};


        /// <summary>
        /// Used as flags during importation of a paddock
        /// </summary>
        private bool surfOMExists = false;

        /// <summary>
        /// Used as flags during importation of a paddock
        /// </summary>
        private bool soilWaterExists = false;

        /// <summary>
        /// Used as flags during importation
        /// </summary>
        private bool microClimateExists = false;

        /// <summary>
        /// Used as flags during importation
        /// </summary>
        private DateTime startDate = DateTime.MinValue;

        /// <summary>
        /// fertiliser type conversion lookups
        /// </summary>
        private Dictionary<string, string> fertilisers;

        /// <summary>
        /// Original path that is substituted for %apsim%
        /// </summary>
        public string ApsimPath = string.Empty;

        /// <summary>
        /// The default constructor
        /// </summary>
        public Importer()
        {
            // fertiliser type strings that are mapped to Fertiliser.Types
            this.fertilisers = new Dictionary<string, string>();
            this.fertilisers.Add("calcite_ca", "CalciteCA");
            this.fertilisers.Add("calcite_fine", "CalciteFine");
            this.fertilisers.Add("dolomite", "Dolomite");
            this.fertilisers.Add("NO3_N", "NO3N");
            this.fertilisers.Add("NH4_N", "NH4N");
            this.fertilisers.Add("NH4NO3", "NH4NO3N");
            this.fertilisers.Add("DAP", "DAP");
            this.fertilisers.Add("MAP", "MAP");
            this.fertilisers.Add("urea_N", "UreaN");
            this.fertilisers.Add("UAN_N", "UAN_N");
            this.fertilisers.Add("urea_no3", "UreaNO3");
            this.fertilisers.Add("urea", "Urea");
            this.fertilisers.Add("nh4so4_n", "NH4SO4N");
            this.fertilisers.Add("rock_p", "RockP");
            this.fertilisers.Add("banded_p", "BandedP");
            this.fertilisers.Add("broadcast_p", "BroadcastP");
        }

        /// <summary>
        /// Processes a file and writes the Simulation(s) to the .apsimx file
        /// </summary>
        /// <param name="filename">The name of the input file</param>
        public void ProcessFile(string filename)
        {
            if (File.Exists(filename))
            {
                try
                {
                    Simulations sims = this.CreateSimulations(filename);
                    sims.Write(Path.ChangeExtension(filename, ".apsimx"));
                    Console.WriteLine(filename + " --> " + Path.ChangeExtension(filename, ".apsimx"));
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                    throw new Exception(e.ToString());
                }
            }
            else
            {
                Console.WriteLine("Input file: " + filename + " cannot be found!");
            }
        }

        /// <summary>
        /// Iterate through the directory and attempt to convert any .apsim files.
        /// </summary>
        /// <param name="dir">The directory to process</param>
        public void ProcessDir(string dir)
        {
            DirectoryInfo dirInfo = new DirectoryInfo(dir);

            foreach (FileInfo file in dirInfo.GetFiles("*.apsim"))
            {
                this.ProcessFile(file.FullName);
            }
        }

        /// <summary>
        /// Interrogate the .apsim file XML and attempt to construct a
        /// useful APSIMX Simulation object(s). Uses a temporary file
        /// location.
        /// </summary>
        /// <param name="filename">Source file (.apsim)</param>
        /// <returns>An APSIMX Simulations object</returns>
        public Simulations CreateSimulations(string filename)
        {
            return CreateSimulationsFromXml(File.ReadAllText(filename));
        }

        /// <summary>
        /// Interrogate the .apsim file XML and attempt to construct a
        /// useful APSIMX Simulation object(s). Uses a temporary file
        /// location.
        /// </summary>
        /// <param name="xml">Source APSIM 7.10 xml</param>
        /// <returns>An APSIMX Simulations object</returns>
        public Simulations CreateSimulationsFromXml(string xml)
        {
            string xfile = Path.GetTempFileName();
            Simulations newSimulations = null;

            // open the .apsim file
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xml);

            Shortcuts.Remove(doc.DocumentElement);

            // create new apsimx document
            XmlNode newNode;
            XmlDocument xdoc = new XmlDocument();
            XmlNode xdocNode = xdoc.CreateElement("Simulations");
            xdoc.AppendChild(xdocNode);
            newNode = xdocNode.AppendChild(xdoc.CreateElement("Name"));
            XmlUtilities.SetAttribute(xdoc.DocumentElement, "Version", XmlConverters.LastVersion.ToString());
            newNode.InnerText = "Simulations";

            XmlNode rootNode = doc.DocumentElement;     // get first folder
            this.AddFoldersAndSimulations(rootNode, xdocNode);
            this.AddDataStore(xdocNode);                     // each file must contain a DataStore

            // write to temporary xfile
            StreamWriter xmlWriter = new StreamWriter(xfile);
            xmlWriter.Write(XmlUtilities.FormattedXML(xdoc.OuterXml));
            xmlWriter.Close();

            newSimulations = FileFormat.ReadFromFile<Simulations>(xfile, e => throw e, false);
            File.Delete(xfile);
            return newSimulations;
        }

        /// <summary>
        /// At the top level we only want to add folder and simulation nodes, not nodes
        /// under 'shared' folders e.g. wheat validation.
        /// </summary>
        /// <param name="systemNode">The root system node</param>
        /// <param name="destParent">The new destination node</param>
        private void AddFoldersAndSimulations(XmlNode systemNode, XmlNode destParent)
        {
            XmlNode child = systemNode.FirstChild;
            while (child != null)
            {
                if (child.Name == "simulation")
                    this.AddComponent(child, ref destParent);
                else if (child.Name == "folder")
                {
                    XmlNode newFolder = this.AddCompNode(destParent, "Folder", XmlUtilities.NameAttr(child));
                    this.AddFoldersAndSimulations(child, newFolder);
                }
                else
                {
                    this.AddComponent(child, ref destParent);
                }
                child = child.NextSibling;
            }
        }

        /// <summary>
        /// Iterate through the child nodes
        /// </summary>
        /// <param name="systemNode">The root system node</param>
        /// <param name="destParent">The new destination node</param>
        private void AddChildComponents(XmlNode systemNode, XmlNode destParent)
        {
            XmlNode child = systemNode.FirstChild;
            while (child != null)
            {
                this.AddComponent(child, ref destParent);
                child = child.NextSibling;
            }
        }

        /// <summary>
        /// Add a component to the xml tree. Customised for each component type
        /// found in the source.
        /// </summary>
        /// <param name="compNode">The source component node in the .apsim file</param>
        /// <param name="destParent">The parent of the new component in the .apsimx file</param>
        /// <returns>The new component xml node</returns>
        public XmlNode AddComponent(XmlNode compNode, ref XmlNode destParent)
        {
            XmlNode newNode = null;
            try
            {
                if (compNode.Name == "simulation")
                {
                    XmlNode newSim = this.AddCompNode(destParent, "Simulation", XmlUtilities.NameAttr(compNode));
                    this.AddChildComponents(compNode, newSim);
                    AddCompNode(newSim, "SoilArbitrator", "SoilArbitrator");
                }
                else if (compNode.Name == "folder" || compNode.Name == "GraphReport")
                {
                    XmlNode newFolder = this.AddCompNode(destParent, "Folder", XmlUtilities.NameAttr(compNode));
                    this.AddChildComponents(compNode, newFolder);
                }
                else if (compNode.Name == "clock")
                {
                    newNode = this.ImportClock(compNode, destParent, newNode);
                }
                else if (compNode.Name == "metfile")
                {
                    newNode = this.ImportMetFile(compNode, destParent, newNode);
                }
                else if (compNode.Name == "micromet")
                {
                    newNode = this.ImportMicromet(compNode, destParent, newNode);
                }
                else if (compNode.Name == "manager")
                {
                    newNode = this.ImportManager(compNode, destParent, newNode);
                }
                else if (compNode.Name == "manager2")
                {
                    newNode = this.ImportManager2(compNode, destParent, newNode);
                }
                else if (compNode.Name == "outputfile")
                {
                    newNode = this.ImportOutputFile(compNode, destParent, newNode);
                }
                else if (compNode.Name == "operations")
                {
                    newNode = this.ImportOperations(compNode, destParent, newNode);
                }
                else if (compNode.Name == "summaryfile")
                {
                    newNode = this.AddCompNode(destParent, "Summary", XmlUtilities.NameAttr(compNode));
                    XmlNode childNode = newNode.AppendChild(destParent.OwnerDocument.CreateElement("html"));
                    childNode.InnerText = "true";
                }
                else if (compNode.Name.ToLower() == "soil")
                {
                    newNode = this.ImportSoil(compNode, destParent, newNode);
                }
                else if (compNode.Name.ToLower() == "sample")
                {
                    newNode = CopyNode(compNode, destParent, "Sample");
                    StripMissingValues(newNode, "Depth");
                    StripMissingValues(newNode, "Thickness");
                    StripMissingValues(newNode, "NO3");
                    StripMissingValues(newNode, "NH4");
                    StripMissingValues(newNode, "SW");
                    StripMissingValues(newNode, "OC");
                    StripMissingValues(newNode, "EC");
                    StripMissingValues(newNode, "PH");
                    StripMissingValues(newNode, "CL");
                    StripMissingValues(newNode, "ESP");
                }
                else if (compNode.Name.ToLower() == "water")
                {
                    newNode = this.ImportWater(compNode, destParent, newNode);
                }
                else if (compNode.Name.ToLower() == "layerstructure")
                {
                    newNode = CopyNode(compNode, destParent, "LayerStructure");
                }
                else if (compNode.Name.ToLower() == "swim")
                {
                    newNode = CopyNode(compNode, destParent, "Swim3");
                    this.AddCompNode(destParent, "CERESSoilTemperature", "Temperature");
                    AddNutrients(compNode, ref destParent);
                }
                else if (compNode.Name.ToLower() == "soilwater")
                {
                    newNode = CopyNode(compNode, destParent, "SoilWater");
                    this.soilWaterExists = newNode != null;
                    this.AddCompNode(destParent, "CERESSoilTemperature", "Temperature");
                    AddNutrients(compNode, ref destParent);
                }
                else if (compNode.Name == "InitialWater")
                {
                    newNode = CopyNode(compNode, destParent, "InitialWater");
                }
                else if (compNode.Name == "SoilOrganicMatter")
                {
                    newNode = CopyNode(compNode, destParent, "SoilOrganicMatter");
                }
                else if (compNode.Name == "Analysis")
                {
                    newNode = CopyNode(compNode, destParent, "Analysis");
                    StripMissingValues(newNode, "Depth");
                    StripMissingValues(newNode, "Thickness");
                    StripMissingValues(newNode, "EC");
                    StripMissingValues(newNode, "PH");
                    StripMissingValues(newNode, "CL");
                    StripMissingValues(newNode, "ESP");
                }
                else if (compNode.Name == "SoilCrop")
                {
                    newNode = CopyNode(compNode, destParent, "SoilCrop");
                }
                else if (compNode.Name == "area")
                {
                    newNode = this.AddCompNode(destParent, "Zone", XmlUtilities.NameAttr(compNode));
                    XmlNode newPaddockNode = newNode;

                    string area = this.GetInnerText(compNode, "paddock_area");
                    if (area == string.Empty)
                    {
                        XmlUtilities.SetValue(compNode, "paddock_area", "1.0");
                    }
                    this.CopyNodeAndValue(compNode, newPaddockNode, "paddock_area", "Area", true);
                    this.surfOMExists = false;
                    this.soilWaterExists = false;
                    this.microClimateExists = false;

                    // copy all the children in this paddock
                    this.AddChildComponents(compNode, newPaddockNode);

                    // if it contains a soilwater then
                    if (this.soilWaterExists && !this.surfOMExists)
                    {
                        Console.WriteLine("Added SurfaceOM to " + XmlUtilities.FullPathUsingName(newPaddockNode));
                        newNode = this.AddCompNode(destParent, "SurfaceOrganicMatter", "SurfaceOrganicMatter");
                        XmlUtilities.SetValue(newNode, "ResourceName", "SurfaceOrganicMatter");
                        XmlUtilities.SetValue(newNode, "InitialResidueName", "wheat_stubble");
                        XmlUtilities.SetValue(newNode, "InitialResidueType", "wheat");
                        XmlUtilities.SetValue(newNode, "InitialResidueMass", "0");
                        XmlUtilities.SetValue(newNode, "InitialCNR", "80");
                    }

                    // alway ensure that MicroClimate exists in this paddock
                    if (!this.microClimateExists)
                    {
                        newNode = this.ImportMicromet(null, newPaddockNode, newNode);    // create a new node from no source
                    }
                }
                else if (compNode.Name == "surfaceom")
                {
                    newNode = this.ImportSurfaceOM(compNode, destParent, newNode);
                    this.surfOMExists = newNode != null;
                }
                else if (compNode.Name == "memo")
                {
                    newNode = this.AddCompNode(destParent, "Memo", XmlUtilities.NameAttr(compNode));
                    XmlNode memoTextNode = newNode.AppendChild(destParent.OwnerDocument.CreateElement("MemoText"));
                    memoTextNode.InnerText = compNode.InnerText;
                }
                else if (compNode.Name == "Graph")
                {
                    newNode = this.ImportGraph(compNode, destParent, newNode);
                }
                else if (StringUtilities.IndexOfCaseInsensitive(cropNames, compNode.Name) != -1)
                {
                    this.ImportPlant(compNode, destParent, newNode);
                }
                else if (string.Equals("irrigation", compNode.Name, StringComparison.InvariantCultureIgnoreCase))
                    AddCompNode(destParent, "Irrigation", "Irrigation");
                else if (string.Equals("fertiliser", compNode.Name, StringComparison.InvariantCultureIgnoreCase))
                    AddCompNode(destParent, "Fertiliser", "Fertiliser");
                else
                {
                    // Do nothing.
                }
            }
            catch (Exception exp)
            {
                throw new Exception($"Cannot import {compNode.Name}.", exp);
            }
            return newNode;
        }

        /// <summary>
        /// Add the Nutrient component and NO3, NH4, UREA
        /// </summary>
        /// <param name="compNode">The source component node in the .apsim file</param>
        /// <param name="destParent">The parent of the new component in the .apsimx file</param>
        private void AddNutrients(XmlNode compNode, ref XmlNode destParent)
        {
            this.AddCompNode(destParent, "Nutrient", "Nutrient");
            XmlNode newNO3Node = this.AddCompNode(destParent, "Solute", "NO3");
            XmlNode newNH4Node = this.AddCompNode(destParent, "Solute", "NH4");
            XmlNode newUREANode = this.AddCompNode(destParent, "Solute", "UREA");

            XmlNode srcNode = XmlUtilities.FindByType(compNode.ParentNode, "Sample");


            if (srcNode != null)
            {
                XmlNode arrayNode;

                // values are ppm
                // find soil layers and values for NO3
                arrayNode = XmlUtilities.Find(srcNode, "Thickness");
                this.CopyNodeAndValueArray(arrayNode, newNO3Node, "Thickness", "Thickness");
                arrayNode = XmlUtilities.Find(srcNode, "NO3");
                this.CopyNodeAndValueArray(arrayNode, newNO3Node, "NO3", "InitialValues");

                var layers = 0;

                foreach (var _ in arrayNode)
                    layers += 1;

                this.InitNodeValueArray(newNO3Node, "EXCO", layers, 0);
                this.InitNodeValueArray(newNO3Node, "FIP", layers, 1);

                // find soil layers and values for NH4
                srcNode = XmlUtilities.FindByType(compNode.ParentNode, "Sample");
                arrayNode = XmlUtilities.Find(srcNode, "Thickness");
                this.CopyNodeAndValueArray(arrayNode, newNH4Node, "Thickness", "Thickness");
                arrayNode = XmlUtilities.Find(srcNode, "NH4");
                this.CopyNodeAndValueArray(arrayNode, newNH4Node, "NH4", "InitialValues");

                this.InitNodeValueArray(newNH4Node, "EXCO", layers, 100);
                this.InitNodeValueArray(newNH4Node, "FIP", layers, 1);

                // find soil layers for UREA
                srcNode = XmlUtilities.FindByType(compNode.ParentNode, "Sample");
                arrayNode = XmlUtilities.Find(srcNode, "Thickness");
                this.CopyNodeAndValueArray(arrayNode, newUREANode, "Thickness", "Thickness");

                this.InitNodeValueArray(newUREANode, "EXCO", layers, 0);
                this.InitNodeValueArray(newUREANode, "FIP", layers, 1);

                // initialise the UREA with some default values
                InitNodeValueArray(newUREANode, "InitialValues", arrayNode.ChildNodes.Count, 0.0);
                XmlNode childNode = newUREANode.AppendChild(newUREANode.OwnerDocument.CreateElement("InitialValuesUnits"));
                if (childNode != null)
                {
                    childNode.InnerText = "1";  // ensure kg/ha units
                }
            }
        }

        private void StripMissingValues(XmlNode newNode, string arrayName)
        {
            var values = XmlUtilities.Values(newNode, arrayName + "/double");
            var indexOfFirstMissing = values.FindIndex(value => string.IsNullOrEmpty(value) || value == "999999" || value == "NaN");
            if (indexOfFirstMissing != -1)
            {
                values.RemoveRange(indexOfFirstMissing, values.Count - indexOfFirstMissing);
                XmlUtilities.SetValues(newNode, arrayName + "/double", values);
            }
        }

        /// <summary>
        /// Import a Plant component
        /// </summary>
        /// <param name="compNode">The node being imported from the apsim file xml</param>
        /// <param name="destParent">Destination parent node that the new child is added to</param>
        /// <param name="plantNode">The new created node</param>
        /// <returns>The newly created node</returns>
        private XmlNode ImportPlant(XmlNode compNode, XmlNode destParent, XmlNode plantNode)
        {
            string name = XmlUtilities.NameAttr(compNode);
            plantNode = this.AddCompNode(destParent, "Plant", name);
            XmlUtilities.SetValue(plantNode, "ResourceName", StringUtilities.CamelCase(name));
            XmlUtilities.SetValue(plantNode, "CropType", "name");

            return plantNode;
        }

        /// <summary>
        /// Unused function
        /// </summary>
        /// <param name="plantNode">The new plant node</param>
        /// <param name="model">The model object</param>
        private void AddLinkedObjects(XmlNode plantNode, object model)
        {
            // Go looking for [Link]s
            foreach (FieldInfo field in ReflectionUtilities.GetAllFields(
                                                                        model.GetType(),
                                                                        BindingFlags.Instance | BindingFlags.FlattenHierarchy | BindingFlags.NonPublic | BindingFlags.Public))
            {
                LinkAttribute link = ReflectionUtilities.GetAttribute(field, typeof(LinkAttribute), false) as LinkAttribute;
                if (link != null)
                {
                    if (!field.FieldType.IsAbstract && !link.IsOptional)
                    {
                        // get the type here and add a tag?
                        XmlNode newNode = this.AddCompNode(plantNode, field.FieldType.Name, field.Name);

                        object obj = Activator.CreateInstance(field.FieldType);
                        this.AddLinkedObjects(newNode, obj);
                    }
                }
            }
        }

        /// <summary>
        /// Import a micromet component
        /// </summary>
        /// <param name="compNode">The node being imported from the apsim file xml</param>
        /// <param name="destParent">Destination parent node that the new child is added to</param>
        /// <param name="newNode">The new micromet node</param>
        /// <returns>The new node</returns>
        private XmlNode ImportMicromet(XmlNode compNode, XmlNode destParent, XmlNode newNode)
        {
            newNode = this.AddCompNode(destParent, "MicroClimate", "MicroClimate");
            if (compNode != null)
            {
                XmlUtilities.SetValue(newNode, "soil_albedo", GetInnerText(compNode, "soilalbedo"));
                XmlUtilities.SetValue(newNode, "a_interception", GetInnerText(compNode, "a_interception"));
                XmlUtilities.SetValue(newNode, "b_interception", GetInnerText(compNode, "b_interception"));
                XmlUtilities.SetValue(newNode, "c_interception", GetInnerText(compNode, "c_interception"));
                XmlUtilities.SetValue(newNode, "d_interception", GetInnerText(compNode, "d_interception"));
            }
            else
            {
                XmlUtilities.SetValue(newNode, "soil_albedo", "0.3");
                XmlUtilities.SetValue(newNode, "a_interception", "0.0");
                XmlUtilities.SetValue(newNode, "b_interception", "1.0");
                XmlUtilities.SetValue(newNode, "c_interception", "0.0");
                XmlUtilities.SetValue(newNode, "d_interception", "0.0");
            }

            this.microClimateExists = true; // has been added

            return newNode;
        }

        /// <summary>
        /// Import surfaceom
        /// </summary>
        /// <param name="compNode">The node being imported from the apsim file xml</param>
        /// <param name="destParent">Destination parent node that the new child is added to</param>
        /// <param name="newNode">The new surfaceom node</param>
        /// <returns>The new node</returns>
        private XmlNode ImportSurfaceOM(XmlNode compNode, XmlNode destParent, XmlNode newNode)
        {
            newNode = this.AddCompNode(destParent, "SurfaceOrganicMatter", "SurfaceOrganicMatter");
            XmlUtilities.SetValue(newNode, "ResourceName", "SurfaceOrganicMatter");
            XmlUtilities.SetValue(newNode, "InitialResidueName", this.GetInnerText(compNode, "PoolName"));
            XmlUtilities.SetValue(newNode, "InitialResidueType", this.GetInnerText(compNode, "type"));
            XmlUtilities.SetValue(newNode, "InitialResidueMass", this.GetInnerText(compNode, "mass"));
            XmlUtilities.SetValue(newNode, "InitialCNR", this.GetInnerText(compNode, "cnr"));
            string cpr = this.GetInnerText(compNode, "cpr");
            if (cpr != string.Empty)
                XmlUtilities.SetValue(newNode, "InitialCPR", cpr);
            XmlUtilities.SetValue(newNode, "InitialStandingFraction", this.GetInnerText(compNode, "standing_fraction"));

            return newNode;
        }

        /// <summary>
        /// Import a graph object
        /// </summary>
        /// <param name="compNode">The node being imported from the apsim file xml</param>
        /// <param name="destParent">Destination parent node that the new child is added to</param>
        /// <param name="newNode">The new graph node</param>
        /// <returns>The new node</returns>
        private XmlNode ImportGraph(XmlNode compNode, XmlNode destParent, XmlNode newNode)
        {
            newNode = this.AddCompNode(destParent, "Graph", XmlUtilities.NameAttr(compNode));

            foreach (XmlNode child in compNode)
            {
                Console.WriteLine("compNode child");
                Console.WriteLine(XmlUtilities.Serialise(child, false));
            }

            // Try and add basic series plots
            foreach (XmlNode plot in XmlUtilities.ChildNodes(compNode, "Plot"))
            {
                Console.WriteLine(XmlUtilities.Serialise(plot, false));
                XmlNode series = destParent.OwnerDocument.CreateElement("Series");

                XmlUtilities.SetNameAttr(series, XmlUtilities.NameAttr(plot));

                XmlNode xNode = XmlUtilities.FindByType(plot, "X");
                XmlNode yNode = XmlUtilities.FindByType(plot, "Y");

                Console.WriteLine("xNode");
                Console.WriteLine(XmlUtilities.Serialise(xNode, false));

                if (xNode != null)
                {
                    XmlNode xn = destParent.OwnerDocument.CreateElement("XFieldName");
                    xn.InnerText = xNode.InnerText;
                    series.AppendChild(xn);
                }

                if (yNode != null)
                {
                    XmlNode yn = destParent.OwnerDocument.CreateElement("YFieldName");
                    yn.InnerText = yNode.InnerText;
                    series.AppendChild(yn);
                }

                //Console.WriteLine(XmlUtilities.Serialise(series, false));

                newNode.AppendChild(series);
            }

            return newNode;
        }

        /// <summary>
        /// Converts a classic field name to an NG equivalent, if one exists.
        /// </summary>
        /// <param name="fieldName">Classic field name.</param>
        /// <returns>True if converted OK, false otherwise</returns>
        private bool ToNGField(ref string fieldName)
        {
            if (string.Compare(fieldName, "Date") == 0)
                fieldName = "[Clock].Today";
            else
                return false;

            return true;
        }


        /// <summary>
        /// Copy a node to a parent node. Has option of specifying a new node name.
        /// </summary>
        /// <param name="compNode">The node being imported from the apsim file xml.</param>
        /// <param name="destParent">Destination parent node that the new child is added to.</param>
        /// <param name="newNodeName">New node name.</param>
        /// <returns>The new node.</returns>
        private XmlNode CopyNode(XmlNode compNode, XmlNode destParent, string newNodeName)
        {
            XmlNode newNode = destParent.OwnerDocument.CreateElement(newNodeName);
            destParent.AppendChild(newNode);
            newNode.InnerXml = compNode.InnerXml;
            XmlUtilities.SetValue(newNode, "Name", XmlUtilities.NameAttr(compNode));
            return newNode;
        }

        /// <summary>
        /// Import the water component
        /// </summary>
        /// <param name="compNode">The node being imported from the apsim file xml</param>
        /// <param name="destParent">Destination parent node that the new child is added to</param>
        /// <param name="newNode">The new water node</param>
        /// <returns>The new node</returns>
        private XmlNode ImportWater(XmlNode compNode, XmlNode destParent, XmlNode newNode)
        {
            newNode = this.AddCompNode(destParent, "Water", XmlUtilities.NameAttr(compNode));

            XmlNode childNode;

            // thickness array
            childNode = XmlUtilities.Find(compNode, "Thickness");
            this.CopyNodeAndValueArray(childNode, newNode, "Thickness", "Thickness");

            // NO3 array
            childNode = XmlUtilities.Find(compNode, "BD");
            this.CopyNodeAndValueArray(childNode, newNode, "BD", "BD");

            // NH4 array
            childNode = XmlUtilities.Find(compNode, "AirDry");
            this.CopyNodeAndValueArray(childNode, newNode, "AirDry", "AirDry");

            // SW array
            childNode = XmlUtilities.Find(compNode, "LL15");
            this.CopyNodeAndValueArray(childNode, newNode, "LL15", "LL15");
            childNode = XmlUtilities.Find(compNode, "DUL");
            this.CopyNodeAndValueArray(childNode, newNode, "DUL", "DUL");
            childNode = XmlUtilities.Find(compNode, "SAT");
            this.CopyNodeAndValueArray(childNode, newNode, "SAT", "SAT");
            childNode = XmlUtilities.Find(compNode, "KS");
            this.CopyNodeAndValueArray(childNode, newNode, "KS", "KS");

            this.AddChildComponents(compNode, newNode);

            return newNode;
        }

        /// <summary>
        /// Import the soil component
        /// </summary>
        /// <param name="compNode">The node being imported from the apsim file xml</param>
        /// <param name="destParent">Destination parent node that the new child is added to</param>
        /// <param name="newNode">The new soil node</param>
        /// <returns>The new node</returns>
        public XmlNode ImportSoil(XmlNode compNode, XmlNode destParent, XmlNode newNode)
        {
            string name = XmlUtilities.Attribute(compNode, "name");
            if (name == "")
                name = XmlUtilities.Value(compNode, "Name");
            if (name == "")
                name = compNode.Name;
            newNode = this.AddCompNode(destParent, "Soil", name);

            this.CopyNodeAndValue(compNode, newNode, "NO3Units", "NO3Units", false);
            this.CopyNodeAndValue(compNode, newNode, "NH4Units", "NH4Units", false);
            this.CopyNodeAndValue(compNode, newNode, "SWUnits", "SWUnits", false);
            this.CopyNodeAndValue(compNode, newNode, "OCUnits", "OCUnits", false);
            this.CopyNodeAndValue(compNode, newNode, "PHUnits", "PHUnits", false);

            this.CopyNodeAndValue(compNode, newNode, "RecordNumber", "RecordNumber", false);
            this.CopyNodeAndValue(compNode, newNode, "ASCOrder", "ASCOrder", false);
            this.CopyNodeAndValue(compNode, newNode, "ASCSubOrder", "ASCSubOrder", false);
            this.CopyNodeAndValue(compNode, newNode, "SoilType", "SoilType", false);
            this.CopyNodeAndValue(compNode, newNode, "Site", "Site", false);
            this.CopyNodeAndValue(compNode, newNode, "NearestTown", "NearestTown", false);
            this.CopyNodeAndValue(compNode, newNode, "Region", "Region", false);
            this.CopyNodeAndValue(compNode, newNode, "State", "State", false);
            this.CopyNodeAndValue(compNode, newNode, "Country", "Country", false);
            this.CopyNodeAndValue(compNode, newNode, "NaturalVegetation", "NaturalVegetation", false);
            this.CopyNodeAndValue(compNode, newNode, "ApsoilNumber", "ApsoilNumber", false);
            this.CopyNodeAndValue(compNode, newNode, "Latitude", "Latitude", false);
            this.CopyNodeAndValue(compNode, newNode, "Longitude", "Longitude", false);
            this.CopyNodeAndValue(compNode, newNode, "LocationAccuracy", "LocationAccuracy", false);
            this.CopyNodeAndValue(compNode, newNode, "DataSource", "DataSource", false);
            this.CopyNodeAndValue(compNode, newNode, "Comments", "Comments", false);
            this.AddChildComponents(compNode, newNode);

            return newNode;
        }

        /// <summary>
        /// Import the output component
        /// </summary>
        /// <param name="compNode">The node being imported from the apsim file xml</param>
        /// <param name="destParent">The new parent xml node</param>
        /// <param name="newNode">The new component node</param>
        /// <returns>The new node</returns>
        private XmlNode ImportOutputFile(XmlNode compNode, XmlNode destParent, XmlNode newNode)
        {
            // compNode/variables array
            List<XmlNode> nodes = new List<XmlNode>();
            XmlUtilities.FindAllRecursively(compNode, "variable", ref nodes);
            List<string> variableNames = new List<string>();

            var table = new Dictionary<string, string>()
            {
                ["day"] = "[Clock].Today.DayOfYear as day",
                ["NO3_0_30cm"] = "sum([Soil].NO3.AmountInSolution[1:5]) as NO3_0_30cm",
                ["NO3_30_60cm"] = "sum([Soil].NO3.AmountInSolution[6:7]) as NO3_30_60cm",
                ["NO3_0_120cm"] = "sum([Soil].NO3.AmountInSolution[1:10]) as NO3_0_120cm",
                ["NO3_profile"] = "sum([Soil].NO3.AmountInSolution) as NO3_profile",
                ["NH4_0_30cm"] = "sum([Soil].NH4.AmountInSolution[1:5]) as NH4_0_30cm",
                ["NH4_30_60cm"] = "sum([Soil].NH4.AmountInSolution[6:7]) as NH4_30_60cm",
                ["NH4_0_120cm"] = "sum([Soil].NH4.AmountInSolution[1:10]) as NH4_0_120cm",
                ["NH4_profile"] = "sum([Soil].NH4.AmountInSolution[1:10]) as NH4_profile",
                ["dnit_0_30cm"] = "sum([Soil].Nutrient.DenitrifiedN[1:5]) as dnit_0_30cm",
                ["dnit_30_60cm"] = "sum([Soil].Nutrient.DenitrifiedN[6:7]) as dnit_30_60cm",
                ["dnit_profile"] = "sum([Soil].Nutrient.DenitrifiedN) as dnit_profile",
                ["Nnet_0_30cm"] = "sum([Nutrient].MineralisedN[1:5]) as Nnet_0_30cm",
                ["Nnet_30_60cm"] = "sum([Nutrient].MineralisedN[6:7]) as Nnet_30_60cm",
                ["Nnet_profile"] = "sum([Nutrient].MineralisedN) as Nnet_profile",
                ["ST_15cm"] = "[Soil].Temperature.Value[4] as ST_15cm",
                ["ST_45cm"] = "0.5 * sum([Soil].Temperature.Value[6:7]) as ST_45cm",
                ["ST_125cm"] = "0.5 * sum([Soil].Temperature.Value[10:11]) as ST_125cm",
                ["SW15cm"] = "[Soil].Water.MM[4] as SW15cm",
                ["DUL15cm"] = "[Soil].Physical.DUL[4] as DUL15cm",
                ["LL15cm"] = "[Soil].Physical.LL15[4] as LL15cm",
                ["SAT15cm"] = "[Soil].Physical.SAT[4] as SAT15cm",
                ["SW45cm"] = "0.5 * sum([Soil].Water.MM[6:7]) as SW45cm",
                ["DUL45cm"] = "0.5 * sum([Soil].Physical.DUL[6:7]) as DUL45cm",
                ["LL45cm"] = "0.5 * sum([Soil].Physical.LL15[6:7]) as LL45cm",
                ["SAT45cm"] = "0.5 * sum([Soil].Physical.SAT[6:7]) as SAT45cm",
                ["SW95cm"] = "[Soil].Water.MM[10] as SW95cm",
                ["SW135cm"] = "[Soil].Water.MM[12] as SW135cm",
                ["watertable"] = "[Swim].WaterTable as watertable",
                ["drain"] = "[Swim].Drainage as drain",
                ["fom_c()"] = "sum([Soil].Nutrient.FOM.C) as fom_c",
                ["biom_c()"] = "sum([Soil].Nutrient.Microbial.C) as biom_c",
                ["hum_c()"] = "sum([Soil].Nutrient.Humic.C) as hum_c",
                ["tile_flow_sum"] = "sum of [Swim].SubsurfaceDrain from [Clock].StartOfYear to [Clock].EndOfYear as tile_flow_sum",
                ["tile_no3_sum"] = "sum of [Swim].SubsurfaceDrainNO3 from [Clock].StartOfYear to [Clock].EndOfYear as tile_no3_sum",
                ["tile_ncon"] = "divide(tile_no3_sum, tile_flow_sum, 0.0)*100 as tile_ncon",
                ["subsurface_drain_no3 as tile_flow"] = "[Swim].SubsurfaceDrain as tile_flow",
                ["subsurface_drain_no3 as tile_no3"] = "[Swim].SubsurfaceDrainNO3 as tile_no3",
                ["crop_buac"] = "([Maize].Grain.Wt * 10 * 0.0159/0.85 + [Soybean].Grain.Wt * 10 * 0.0149 / 0.87) as crop_buac",
                ["LAI_crop"] = "[Maize].LAI + [Soybean].LAI as LAI_crop",
                ["Biomass_crop"] = "([Maize].AboveGround.Wt + [Soybean].AboveGround.Wt) * 10 as Biomass_crop",
                ["Yield_crop"] = "([Maize].Grain.Wt + [Soybean].AboveGround.Wt) * 10 as Yield_crop",
                ["Stover_crop"] = "(Biomass_crop - Yield_crop) as Stover_crop",
                ["BiomassN_crop"] = "[Maize].AboveGround.N + [Soybean].AboveGround.N as BiomassN_crop",
                ["YieldN_crop"] = "[Maize].Grain.N + [Soybean].AboveGround.N as YieldN_crop",
                ["StoverN_crop"] = "(BiomassN_crop - YieldN_crop) as StoverN_crop",
                ["GrainSize_crop"] = "[Maize].Grain.Size + [Soybean].Grain.Size as GrainSize_crop",
                ["Fruit_soy"] = "[Soybean].Pod.Wt * 10 as Fruit_soy",
                ["FruitN_soy"] = "[Soybean].Pod.N * 10 as FruitN_soy",
                ["StoverCN_crop"] = "divide(Stover_crop * 0.4, StoverN_crop, 0.0) as StoverCN_crop",
                ["RootMass_crop"] = "10 * ([Maize].Root.Wt + [Soybean].Root.Wt) as RootMass_crop",
                ["RootNconc_crop"] = "[Maize].Root.Nconc + [Soybean].Root.Nconc as RootNconc_crop",
                ["RootN_crop"] = "10 * ([Maize].Root.N + [Soybean].Root.N) as RootN_crop",
                ["Root_in"] = "[Maize].Root.Depth + [Soybean].Root.Depth as Root_in",
                ["BiomassG_crop"] = "[Maize].AboveGroundLive.Wt + [Soybean].AboveGroundLive.Wt as BiomassG_crop",
                ["Leaf_pot"] = "[Maize].Leaf.AppearedCohortNo as Leaf_pot",
                ["LeafNode"] = "[Maize].Leaf.AppearedCohortNo - [Maize].Leaf.DeadCohortNo + [Soybean].Leaf.NodeNumber.Value() as LeafNode",
                ["PlantNo"] = "[Maize].Population + [Soybean].Population as PlantNo",
                ["Soy_seedsize"] = "[Soybean].Grain.Size as Soy_seedsize",
                ["soybean.node_no as SoyNodes"] = "[Soybean].Leaf.NodeNumber.Value() as SoyNodes",
                ["maize.biomass as CornBiom"] = "[Maize].AboveGround.Wt * 10 as CornBiom",
                ["maize.yield as CornYield"] = "[Maize].Grain.Wt * 10 as CornYield",
                ["maize.lai as CornLAI"] = "[Maize].LAI as CornLAI",
                ["CornNupt"] = "[Maize].AboveGround.N * 10 as CornNupt",
                ["soybean.biomass as SoyBiom"] = "[Soybean].AboveGround.Wt * 10 as SoyBiom",
                ["soybean.lai as SoyLAI"] = "[Soybean].LAI as SoyLAI",
                ["SoyNupt"] = "[Soybean].AboveGround.N * 10 as SoyNupt",
                ["surfaceom_wt"] = "[SurfaceOrganicMatter].Wt as surfaceom_wt",
                ["surfaceom_c"] = "[SurfaceOrganicMatter].C as surfaceom_c",
                ["surfaceom_n"] = "[SurfaceOrganicMatter].N as surfaceom_n",
                ["surfaceom_cover"] = "[SurfaceOrganicMatter].Cover as surfaceom_cover",
                ["residueCN"] = "divide(surfaceom_c, surfaceom_n, 0.0) as residueCN",
                ["year"] = "[Clock].Today.Year as year",
                ["AnnualRunoff"] = "sum of [Swim].Runoff from [Clock].StartOfYear to [Clock].EndOfYear as AnnualRunoff",
                ["AnnualRain"] = "sum of [Weather].Rain from [Clock].StartOfYear to [Clock].EndOfYear as AnnualRain",
                ["AnnualDnit"] = "sum of sum([Soil].Nutrient.DenitrifiedN) from [Clock].StartOfYear to [Clock].EndOfYear as AnnualDnit",
                ["AnnualNetMiner"] = "sum of sum([Soil].Nutrient.MineralisedN) from [Clock].StartOfYear to [Clock].EndOfYear as AnnualNetMiner",
                ["AnnualDrain"] = "sum of [SWIM].Drainage from [Clock].StartOfYear to [Clock].EndOfYear as AnnualDrain",
                ["InSeason_Radiation"] = "sum of [Weather].Radn from [Plant].Sowing to [Plant].Harvesting as InSeason_Radiation",
                ["ET_annual"] = "sum of ([Swim].Es + [Maize].Leaf.Transpiration + [Soybean].Leaf.Transpiration) from [Clock].StartOfYear to [Clock].EndOfYear as ET_annual",
                ["T_annual"] = "sum of ([Maize].Leaf.Transpiration + [Soybean].Leaf.Transpiration) from [Clock].StartOfYear to [Clock].EndOfYear as T_annual",
                ["H20FlowB120cm"] = "sum of [Swim].Flow[11] from [Clock].StartOfYear to [Clock].EndOfYear as H20FlowB120cm",
                ["NO3FlowB120cm"] = "sum of [Swim].FlowNO3[11] from [Clock].StartOfYear to [Clock].EndOfYear as NO3FlowB120cm",
                // TODO:
                // Ngross_30_cm, Ngross_60_cm, Ngross_profile, AnnualGrossMiner. NG does not appear to have NGross.
                // GrainsPots. Not sure how to get pods for soybean.
                // oxdef_photo_crop, oxdef_fix_s. Both unimplemented.
                // swdef_expan_crop, swdef_fixation_s. Don't know how to do NG stresses.
                // nfact_grain_crop, temp_RUE_crop, low_rad_crop, low_mint_crop. All need conditionals
                // SoyPods. See GrainPots.
                // SoyLeaves. SimpleLeaf doesn't appear to have leaf number.
                // SoyFixN. Not sure how to track fixation in NG.
                // TileWaterAnnual, TileNitrateAnnual, root_crop. All undefined.
            };
            foreach (XmlNode var in nodes)
            {
                string varText = var.InnerText;

                if (varText.Contains("yyyy"))
                    variableNames.Add("[Clock].Today as Date");
                else if (table.ContainsKey(varText))
                    variableNames.Add(table[varText]);
                else
                    variableNames.Add("//" + varText);
            }

            // now for the events
            nodes.Clear();
            XmlUtilities.FindAllRecursively(compNode, "event", ref nodes);
            List<string> eventNames = new List<string>();
            foreach (XmlNode theEvent in nodes)
            {
                if (string.Compare(theEvent.InnerText, "end_day", true) == 0)
                    eventNames.Add("[Clock].DoReport");
                else if (string.Compare(theEvent.InnerText, "daily", true) == 0)
                    eventNames.Add("[Clock].DoReport");
                else
                    eventNames.Add("[Clock].DoReport");
            }

            newNode = this.AddCompNode(destParent, "Report", XmlUtilities.NameAttr(compNode));
            XmlNode variablesNode = newNode.AppendChild(newNode.OwnerDocument.CreateElement("VariableNames"));
            XmlUtilities.SetValues(variablesNode, "string", variableNames);

            XmlNode eventsNode = newNode.AppendChild(newNode.OwnerDocument.CreateElement("EventNames"));
            XmlUtilities.SetValues(eventsNode, "string", eventNames);

            return newNode;
        }

        /// <summary>
        /// Find all the Manager script parameters and populate the list
        /// </summary>
        /// <param name="compNode">Manager component node</param>
        /// <param name="scriptParams">The list of extracted parameters</param>
        private void GetManagerParams(XmlNode compNode, List<ScriptParameter> scriptParams)
        {
            CSharpCodeProvider cs = new CSharpCodeProvider();
            List<XmlNode> nodes = new List<XmlNode>();
            XmlUtilities.FindAllRecursivelyByType(compNode, "ui", ref nodes);
            foreach (XmlNode ui in nodes)
            {
                foreach (XmlNode init in ui)
                {
                    string typeName = XmlUtilities.Attribute(init, "type");
                    if ((String.Compare(init.Name, "category", true) != 0) && (String.Compare(typeName, "category", true) != 0))
                    {
                        ScriptParameter param = new ScriptParameter();
                        param.Name = init.Name;
                        string item = param.Name.Trim();
                        if (!cs.IsValidIdentifier(item))    // returns false if this should not be used
                        {
                            param.Name = "_" + param.Name;
                        }
                        param.Value = init.InnerText;
                        param.ListValues = XmlUtilities.Attribute(init, "listvalues");
                        param.Description = XmlUtilities.Attribute(init, "description");
                        param.TypeName = typeName;
                        // Convert any boolean values
                        if (String.Compare(param.TypeName, "yesno") == 0)
                        {
                            if (param.Value.Contains("o"))
                            {
                                param.Value = "false";
                            }
                            else
                            {
                                param.Value = "true";
                            }
                        }
                        else if (String.Compare(param.TypeName, "list") == 0 && !string.IsNullOrEmpty(param.ListValues))
                        {
                            // some enumerated types contain invalid identifiers for C#
                            // - in fact there are some lists of strings which do not convert
                            //   over at all. Not sure what to do with those.
                            string[] items = param.ListValues.Split(',');
                            foreach (string s in items)
                            {
                                item = s.Trim();
                                if (!cs.IsValidIdentifier(item))    // returns false if this should not be used
                                {
                                    param.ListValues = param.ListValues.Replace(item, "_" + item + " /*replaces " + item + "*/");
                                }
                            }
                        }
                        scriptParams.Add(param);
                    }
                }
            }
        }

        /// <summary>
        /// All the extracted Manager parameters are added to an XmlElement[]
        /// </summary>
        /// <param name="scriptParams"></param>
        /// <returns></returns>
        private List<KeyValuePair<string, string>> AddManagerParams(List<ScriptParameter> scriptParams)
        {
            List<KeyValuePair<string, string>> parameters = new List<KeyValuePair<string, string>>();
            foreach (ScriptParameter param in scriptParams)
                parameters.Add(new KeyValuePair<string, string>(param.Name, param.Value));

            return parameters;
        }

        /// <summary>
        /// Write the Manager parameter declarations and enumerated types
        /// into a Manager C# string
        /// </summary>
        /// <param name="scriptParams"></param>
        /// <returns>Manager script section</returns>
        private string WriteManagerParams(List<ScriptParameter> scriptParams)
        {
            StringBuilder code = new StringBuilder();
            List<string> enumTypes = new List<string>();
            // write the declarations of the parameters
            foreach (ScriptParameter param in scriptParams)
            {
                string atype = "string ";   //ddmmmdate, crop, text, cultivar
                if (String.Compare(param.TypeName, "yesno") == 0)
                {
                    atype = "bool ";
                }
                else if (String.Compare(param.TypeName, "list") == 0)
                {
                    //create an enumeration
                    atype = param.Name + "Type ";
                    var enumValues = param.ListValues.Replace("-", "_");
                    enumTypes.Add("\t\tpublic enum " + atype + "\n\t\t{\n\t\t\t" + enumValues + "\n\t\t}\n");
                }

                code.Append("\n\t\t[Description(\"" + param.Description + "\")]\n");
                if (String.Compare(param.TypeName, "cultivars") == 0)
                {
                    code.Append("\t\t[Display(Type = DisplayType.CultivarName)]\n");
                }
                code.Append("\t\tpublic " + atype + param.Name + " { get; set; }\n");
            }

            // write the enumerated types in a block
            foreach (string enumType in enumTypes)
            {
                code.Append(enumType);
            }
            code.Append("\n");

            return code.ToString();
        }

        /// <summary>
        /// Import a Manager(1) component.
        /// </summary>
        /// <param name="compNode">The node being imported from the apsim file xml</param>
        /// <param name="destParent">The new parent xml node</param>
        /// <param name="newNode">The new component node</param>
        /// <returns>The new node</returns>
        private XmlNode ImportManager(XmlNode compNode, XmlNode destParent, XmlNode newNode)
        {
            newNode = this.AddCompNode(destParent, "Manager", XmlUtilities.NameAttr(compNode));

            StringBuilder code = new StringBuilder();
            code.Append("using System;\nusing Models.Core;\nusing Models.PMF;\nusing Models.PMF;\nnamespace Models\n{\n");
            code.Append("\t[Serializable]\n");
            code.Append("\t[System.Xml.Serialization.XmlInclude(typeof(Model))]\n");
            code.Append("\tpublic class Script : Model\n");
            code.Append("\t{\n");
            code.Append("\t\t[Link] Clock Clock;\n");

            List<string> startofdayScripts = new List<string>();
            List<string> endofdayScripts = new List<string>();
            List<string> initScripts = new List<string>();
            List<string> unknownHandlerScripts = new List<string>();
            List<XmlNode> nodes = new List<XmlNode>();

            // Convert the <ui> section
            List<ScriptParameter> scriptParams = new List<ScriptParameter>();
            this.GetManagerParams(compNode, scriptParams);
            XmlNode scriptNode = newNode.AppendChild(newNode.OwnerDocument.CreateElement("Script"));
            foreach (ScriptParameter param in scriptParams)
                XmlUtilities.SetValue(scriptNode, param.Name, param.Value);

            code.Append(this.WriteManagerParams(scriptParams));

            // Convert the <script> section
            XmlUtilities.FindAllRecursivelyByType(compNode, "script", ref nodes);
            foreach (XmlNode script in nodes)
            {
                // find the event
                XmlNode eventNode = XmlUtilities.Find(script, "event");

                // find the text
                XmlNode textNode = XmlUtilities.Find(script, "text");
                if ((textNode != null) && (textNode.InnerText.Length > 0))
                {
                    if (eventNode.InnerText.ToLower() == "init")
                    {
                        initScripts.Add(textNode.InnerText);
                    }
                    else if (eventNode.InnerText.ToLower() == "start_of_day")
                    {
                        startofdayScripts.Add(textNode.InnerText);
                    }
                    else if (eventNode.InnerText.ToLower() == "end_of_day")
                    {
                        endofdayScripts.Add(textNode.InnerText);
                    }
                    else
                    {
                        // use the StartOfDay as a default when the event name is unknown
                        unknownHandlerScripts.Add("// ----- " + eventNode.InnerText + " ----- \n" + textNode.InnerText);
                    }
                }
            }

            // append all the scripts for each type
            if (initScripts.Count > 0)
            {
                code.Append("\t\t[EventSubscribe(\"Commencing\")]\n");
                code.Append("\t\tprivate void OnSimulationCommencing(object sender, EventArgs e)\n");
                code.Append("\t\t{\n");
                foreach (string scripttext in initScripts)
                {
                    code.Append("\t\t\t/*\n");
                    code.Append("\t\t\t\t" + scripttext + "\n");
                    code.Append("\t\t\t*/\n");
                }
                code.Append("\t\t}\n");
            }
            if (startofdayScripts.Count > 0)
            {
                code.Append("\t\t[EventSubscribe(\"DoManagement\")]\n");
                code.Append("\t\tprivate void OnDoManagement(object sender, EventArgs e)\n");
                code.Append("\t\t{\n");
                foreach (string scripttext in startofdayScripts)
                {
                    code.Append("\t\t\t/*\n");
                    code.Append("\t\t\t\t" + scripttext + "\n");
                    code.Append("\t\t\t*/\n");
                }
                code.Append("\t\t}\n");
            }
            if (endofdayScripts.Count > 0)
            {
                code.Append("\t\t[EventSubscribe(\"DoCalculations\")]\n");
                code.Append("\t\tprivate void OnDoCalculations(object sender, EventArgs e)\n");
                code.Append("\t\t{\n");
                foreach (string scripttext in endofdayScripts)
                {
                    code.Append("\t\t\t/*\n");
                    code.Append("\t\t\t\t" + scripttext + "\n");
                    code.Append("\t\t\t*/\n");
                }
                code.Append("\t\t}\n");
            }
            if (unknownHandlerScripts.Count > 0)
            {
                code.Append("\t\t//[EventSubscribe(\"unknown\")]\n");
                code.Append("\t\t//private void OnUnknown(object sender, EventArgs e)\n");
                code.Append("\t\t//{\n");
                foreach (string scripttext in unknownHandlerScripts)
                {
                    code.Append("\t\t\t/*\n");
                    code.Append("\t\t\t\t" + scripttext + "\n");
                    code.Append("\t\t\t*/\n");
                }
                code.Append("\t\t//}\n");
            }
            code.Append("\t}\n}\n");

            XmlNode codeNode = newNode.AppendChild(newNode.OwnerDocument.CreateElement("Code"));
            codeNode.AppendChild(newNode.OwnerDocument.CreateCDataSection(code.ToString()));

            // some Manager components have Memo children. For ApsimX the import
            // will just put them as the next sibling of the Manager rather
            // than as a child of the Manager.
            destParent = this.ImportManagerMemos(compNode, destParent);

            return newNode;
        }

        /// <summary>
        /// Import any memo children.
        /// </summary>
        /// <param name="compNode">The Manager component node</param>
        /// <param name="destParent">The parent (folder) node of the Manager.</param>
        /// <returns>The new node</returns>
        private XmlNode ImportManagerMemos(XmlNode compNode, XmlNode destParent)
        {
            XmlNode child = compNode.FirstChild;
            while (child != null)
            {
                if (child.Name == "memo")
                {
                    this.AddComponent(child, ref destParent);
                }
                child = child.NextSibling;
            }
            return destParent;
        }

        /// <summary>
        /// Import a Manager(2) component.
        /// </summary>
        /// <param name="compNode">The Manager component node being imported</param>
        /// <param name="destParent">Destination parent node that the new child is added to</param>
        /// <param name="newNode">The new Manager node</param>
        /// <returns>The new node</returns>
        private XmlNode ImportManager2(XmlNode compNode, XmlNode destParent, XmlNode newNode)
        {
            newNode = this.AddCompNode(destParent, "Manager", XmlUtilities.NameAttr(compNode));

            // copy code here
            StringBuilder code = new StringBuilder();

            // Convert the <ui> section
            List<ScriptParameter> scriptParams = new List<ScriptParameter>();
            XmlNode scriptNode = newNode.AppendChild(newNode.OwnerDocument.CreateElement("Script"));
            foreach (ScriptParameter param in scriptParams)
                XmlUtilities.SetValue(scriptNode, param.Name, param.Value);

            // find the <text> node
            XmlNode textNode = XmlUtilities.Find(compNode, "text");
            if ((textNode != null) && (textNode.InnerText.Length > 0))
            {
                // comment out depricated includes
                string csharpCode = textNode.InnerText.Replace("using ModelFramework;", "//using ModelFramework;");
                csharpCode = csharpCode.Replace("using CSGeneral;", "//using CSGeneral;");
                csharpCode = csharpCode.Replace("using System.Linq;", "//using System.Linq;");

                // This code comments out the core of the Script class
                // If it is necessary to manipulate the code to make it APSIMX compatible
                // then this is the place to do it.

                int classPos = csharpCode.IndexOf("class Script");
                if (classPos >= 0)
                {
                    int startBody = csharpCode.IndexOf("{", classPos) + 1;
                    int pos = csharpCode.LastIndexOf("public", classPos - 1);
                    string prefix = csharpCode.Substring(0, pos);
                    code.Append(prefix);
                    // replace the includes and setup the namespace
                    code.Append("\nusing Models.Core;\nnamespace Models\n{\n");
                    code.Append("\t[Serializable]\n");
                    code.Append("\t[System.Xml.Serialization.XmlInclude(typeof(Model))]\n");
                    code.Append("\tpublic class Script : Model\n");
                    code.Append("\t{\n");
                    code.Append(this.WriteManagerParams(scriptParams));       // this could be used in the Scipt class
                    code.Append("\t/*\n");

                    int endPos = csharpCode.LastIndexOf("}");
                    code.Append(csharpCode.Substring(startBody, endPos - startBody));
                    code.Append("\n*/\n\t}\n}");
                    string suffix = csharpCode.Substring(endPos + 1, csharpCode.Length - endPos - 1);
                    code.Append(suffix);
                }
            }

            XmlNode codeNode = newNode.AppendChild(newNode.OwnerDocument.CreateElement("Code"));
            codeNode.AppendChild(newNode.OwnerDocument.CreateCDataSection(code.ToString()));

            // some Manager components have Memo children. For ApsimX the import
            // will just put them as the next sibling of the Manager rather
            // than as a child of the Manager.
            destParent = this.ImportManagerMemos(compNode, destParent);

            return newNode;
        }

        /// <summary>
        /// Import the weather object
        /// </summary>
        /// <param name="compNode">The node being imported from the apsim file xml</param>
        /// <param name="destParent">The new parent xml node</param>
        /// <param name="newNode">The new weather component node</param>
        /// <returns>The new component node</returns>
        private XmlNode ImportMetFile(XmlNode compNode, XmlNode destParent, XmlNode newNode)
        {
            newNode = this.AddCompNode(destParent, "Weather", "Weather");

            // compNode/filename value
            XmlNode anode = newNode.AppendChild(destParent.OwnerDocument.CreateElement("FileName"));
            string metfilepath = PathUtilities.OSFilePath(this.GetInnerText(compNode, "filename"));

            anode.InnerText = metfilepath.Replace("%apsim%", this.ApsimPath);

            return newNode;
        }

        /// <summary>
        /// Import Clock
        /// </summary>
        /// <param name="compNode">The node being imported from the apsim file xml</param>
        /// <param name="destParent">The new parent xml node</param>
        /// <param name="newNode">The new clock component node</param>
        /// <returns>The new component node</returns>
        private XmlNode ImportClock(XmlNode compNode, XmlNode destParent, XmlNode newNode)
        {
            newNode = this.AddCompNode(destParent, "Clock", "Clock");

            string startDate = this.GetInnerText(compNode, "start_date");
            string endDate = this.GetInnerText(compNode, "end_date");
            XmlUtilities.SetValue(newNode, "StartDate", DateUtilities.DMYtoDate(startDate).ToString("yyyy-MM-ddT00:00:00"));
            XmlUtilities.SetValue(newNode, "EndDate", DateUtilities.DMYtoDate(endDate).ToString("yyyy-MM-ddT00:00:00"));

            return newNode;
        }

        /// <summary>
        /// Import operations
        /// </summary>
        /// <param name="compNode">The Operations node being imported</param>
        /// <param name="destParent">Destination parent node that the new child is added to</param>
        /// <param name="newNode">The new clock node</param>
        /// <returns>The new node</returns>
        private XmlNode ImportOperations(XmlNode compNode, XmlNode destParent, XmlNode newNode)
        {
            newNode = this.AddCompNode(destParent, "Operations", XmlUtilities.NameAttr(compNode));

            XmlNode childNode;

            List<XmlNode> nodes = new List<XmlNode>();
            XmlUtilities.FindAllRecursively(compNode, "operation", ref nodes);
            foreach (XmlNode oper in nodes)
            {
                string childText = "";
                childNode = XmlUtilities.Find(oper, "action");

                if (childNode != null)
                {
                    childText = childNode.InnerText;

                    // parse the operation and determine if this can be converted to an apsimx function call
                    // ** This code makes a BIG assumption that the fertiliser component has that name. Also the irrigation component!
                    // if this name is not found then the conversion is not done as expected.
                    childText = childText.Trim();
                    int index = childText.IndexOf(" ");
                    if (index > 0)
                    {
                        string operationOn = childText.Substring(0, index);

                        if ((string.Compare(operationOn, "fertiliser", true) == 0) && ((childText.IndexOf(" apply") > 0) || (childText.IndexOf(" Apply") > 0)))
                        {
                            string amount = "0", type = string.Empty, depth = "0";

                            this.FindTokenValue("amount", childText, ref amount);
                            this.FindTokenValue("type", childText, ref type);

                            string newtype;
                            this.fertilisers.TryGetValue(type, out newtype);

                            type = "Fertiliser.Types." + newtype;
                            this.FindTokenValue("depth", childText, ref depth);

                            childText = string.Format("[Fertiliser].Apply({0}, {1}, {2});", amount, type, depth);
                        }
                        else if ((string.Compare(operationOn, "irrigation", true) == 0) && ((childText.IndexOf(" apply") > 0) || (childText.IndexOf(" Apply") > 0)))
                        {
                            string amount = "0";

                            this.FindTokenValue("amount", childText, ref amount);

                            childText = string.Format("[Irrigation].Apply({0});", amount);
                        }
                        else if ((operationOn.IndexOf("surfaceorganicmatter") > 0) && (childText.IndexOf(" tillage") > 0))
                        {
                            string f_incorp = "0", depth = "0";

                            this.FindTokenValue("f_incorp", childText, ref f_incorp);
                            this.FindTokenValue("depth", childText, ref depth);

                            childText = string.Format("[SurfaceOrganicMatter].Incorporate({0}, {1});", f_incorp, depth);
                        }
                        else if ((string.Compare(operationOn, "soybean", true) == 0) || (string.Compare(operationOn, "maize", true) == 0))
                        {
                            childText = ParseCropOperation(operationOn, childText);
                        }
                        else
                        {
                            childText = "// " + childText; // for default comment out the operations code for now
                        }
                    }

                    // Trim again to avoid whitespace operations
                    childText = childText.Trim();

                    if (childText.Length > 0)
                    {
                        XmlNode operationNode = newNode.AppendChild(destParent.OwnerDocument.CreateElement("Operation"));
                        XmlNode dateNode = operationNode.AppendChild(destParent.OwnerDocument.CreateElement("Date"));

                        childNode = XmlUtilities.Find(oper, "date");
                        dateNode.InnerText = OperationDateToNGDate(childNode?.InnerText);

                        XmlNode actionNode = operationNode.AppendChild(destParent.OwnerDocument.CreateElement("Action"));

                        actionNode.InnerText = childText;
                    }
                }
            } // next operation

            return newNode;
        }

        /// <Summary>
        /// Method for converting an Apsim classic crop-based Operation 
        /// into an equivalent NG operation.
        /// </Summary>
        /// <param name="cropName">The name of the crop being acted on</param>
        /// <param name="childText">The original apsim classic operation text</param>
        /// <returns>An operation action string for apsim NG</returns>
        private string ParseCropOperation(string cropName, string childText)
        {
            cropName = char.ToUpper(cropName[0]) + cropName.Substring(1);

            if (childText.IndexOf("end_crop") > 0)
                childText = string.Format("[{0}].EndCrop();", cropName);
            else if (childText.IndexOf("harvest") > 0)
                childText = string.Format("[{0}].Harvest();", cropName);
            else if (childText.IndexOf("sow") > 0)
            {
                string rowSpacing = "0", depth = "0", cultivar = "Generic_MG2", population = "0";

                if (string.Compare(cropName, "Maize") == 0)
                    cultivar = "B_110";

                this.FindTokenValue("plants", childText, ref population);
                this.FindTokenValue("sowing_depth", childText, ref depth);

                // TODO: stronger cultivar selection
                //this.FindTokenValue("cultivar", childText, ref cultivar);

                childText = string.Format("[{0}].Sow({1}, {2}, {3}, {4});", cropName, cultivar, population, depth, rowSpacing);
            }
            else
            {
                throw new System.Exception("bad crop operation");
            }

            return childText;
        }

        /// <Summary>
        /// Method for parsing a date used by a classic Operations manager to
        /// a format recognized by NG Operations.
        /// </Summary>
        /// <param name="date">The (possibly null or empty) date string</param>
        /// <returns>A date string recognized by NG's Operations</returns>
        private string OperationDateToNGDate(string date)
        {
            // Different formats accepted by classic's OperationsSchedule
            // first two options save us from needing to call DMYtoISO
            string[] dateFormatsFixed = {"d/M/yyyy", "d-M-yyyy", "d-MMM-yyyy", "d_MMM_yyyy"};
            string[] dateFormatsAnnual = {"d-MMM", "d_MMM"};

            if (String.IsNullOrEmpty(date))
                return "0001-01-01";

            if (DateTime.TryParseExact(date,
                                       dateFormatsFixed,
                                       CultureInfo.InvariantCulture,
                                       DateTimeStyles.None,
                                       out DateTime when))
                return when.ToString("yyyy-MM-dd");

            if (DateTime.TryParseExact(date,
                                       dateFormatsAnnual,
                                       CultureInfo.InvariantCulture,
                                       DateTimeStyles.None,
                                       out _))
                // Matches a dd-MMM or dd_MMM date
                return date;

            // No matches so far, try our other converters.
            var result = DoyYearToISO(date);
            if (result == "0001-01-01")
                result = DateUtilities.GetDate(date, this.startDate).ToString("yyyy-MM-dd");

            return result;
        }

        /// <summary>
        /// Convdert a doy year or year doy string into a yyyy-MM-dd string.
        /// This was a format accepted by Classic's Operations module.
        /// </summary>
        /// <param name="doyYear">doy yyyy | yyyy doy</param>
        /// <returns>yyyy-MM-dd string</returns>
        private string DoyYearToISO(string doyYear)
        {
            var twoInts = new Regex(@"^(\d+) (\d+)$");
            var m = twoInts.Match(doyYear);
            if (m.Success)
            {
                // Similar behavior to Operation's date converter in classic.
                var doy =  Convert.ToInt32(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var year = Convert.ToInt32(m.Groups[2].Value, CultureInfo.InvariantCulture);
                if (doy > 366)
                {
                    // Might be a yyyy doy string
                    var tmp = doy;
                    doy = year;
                    year = tmp;
                }
                if (doy <= 366)
                {
                    var dt = new DateTime(year, 1, 1);
                    return dt.AddDays(doy - 1).ToString("yyyy-MM-dd");
                }
            }
            // Default value
            return "0001-01-01";
        }

        /// <summary>
        /// Rough method for parsing an old manager script function call to obtain
        /// the parameter values.
        /// </summary>
        /// <param name="name">Name of parameter</param>
        /// <param name="line">Input function call line text</param>
        /// <param name="value">The value found for the parameter. Contains the original value if not found.</param>
        /// <returns>Index of the parameter name found or -1 if not found</returns>
        private int FindTokenValue(string name, string line, ref string value)
        {
            int index = -1; // not found
            index = line.IndexOf(name);
            if (index > 0)
            {
                value = string.Empty;
                int i = index + name.Length;
                while ((i < line.Length) && (line[i] == ' '))
                    i++;

                // find =
                while ((i < line.Length) && (line[i] != '='))
                    i++;
                i++;
                while ((i < line.Length) && (line[i] == ' '))
                    i++;

                // now build the value parameter
                while ((i < line.Length) && (line[i] != ' ') && (line[i] != '(') && (line[i] != ','))
                {
                    value = value + line[i];
                    i++;
                }
            }
            return index;
        }

        /// <summary>
        /// Copy an array of scalars
        /// </summary>
        /// <param name="srcParentNode">The parent node of the array nodes</param>
        /// <param name="destParentNode">Destination parent node that the new child is added to</param>
        /// <param name="srcName">Name of the source array</param>
        /// <param name="destName">Name of the destination array</param>
        private void CopyNodeAndValueArray(XmlNode srcParentNode, XmlNode destParentNode, string srcName, string destName)
        {
            if (srcParentNode != null)
            {
                XmlNode childNode = destParentNode.AppendChild(destParentNode.OwnerDocument.CreateElement(destName));

                XmlNode child = srcParentNode.FirstChild;
                while (child != null)
                {
                    this.CopyNodeAndValue(child, childNode, child.Name);
                    child = child.NextSibling;
                }
            }
        }

        /// <summary>
        /// Copy the <code>srcNode</code> using <code>destName</code>.
        /// </summary>
        /// <param name="srcNode">The source node</param>
        /// <param name="destParentNode">Destination parent node that the new child is added to</param>
        /// <param name="destName">Name of the destination node that will be appended</param>
        private void CopyNodeAndValue(XmlNode srcNode, XmlNode destParentNode, string destName)
        {
            if (srcNode != null)
            {
                XmlNode valNode = destParentNode.AppendChild(destParentNode.OwnerDocument.CreateElement(destName));
                valNode.InnerText = srcNode.InnerText;
            }
        }

        /// <summary>
        /// When copying a single unique node and value
        /// </summary>
        /// <param name="srcParentNode">Parent node of the source node</param>
        /// <param name="destParentNode">Destination parent node that the new child is added to</param>
        /// <param name="srcName">Name of the source node</param>
        /// <param name="destName">Name of the destination node created</param>
        /// <param name="forceCreate">Always create the node even if the source is not found</param>
        private void CopyNodeAndValue(XmlNode srcParentNode, XmlNode destParentNode, string srcName, string destName, bool forceCreate)
        {
            if (srcParentNode != null)
            {
                XmlNode srcChildNode = XmlUtilities.Find(srcParentNode, srcName);
                if (forceCreate || (srcChildNode != null))
                {
                    XmlNode childNode = destParentNode.AppendChild(destParentNode.OwnerDocument.CreateElement(destName));
                    if (srcChildNode != null)
                    {
                        childNode.InnerText = srcChildNode.InnerText;
                    }
                    else
                    {
                        childNode.InnerText = string.Empty;
                    }
                }
            }
        }

        /// <summary>
        /// Create an array of specified size and initialise all elements
        /// to the specified value
        /// </summary>
        /// <param name="destParentNode">Parent xml node</param>
        /// <param name="destName">Name of the new node</param>
        /// <param name="count">Count of child values to add</param>
        /// <param name="value">The init value</param>
        private void InitNodeValueArray(XmlNode destParentNode, string destName, int count, double value)
        {
            XmlNode childNode = destParentNode.AppendChild(destParentNode.OwnerDocument.CreateElement(destName));

            for (int i = 0; i < count; i++)
            {
                XmlNode valNode = childNode.AppendChild(destParentNode.OwnerDocument.CreateElement("double"));
                valNode.InnerText = string.Format("{0:f}", value);
            }
        }

        /// <summary>
        /// Do the mechanical adding of the xml node in the document
        /// </summary>
        /// <param name="parentNode">The parent of the new node</param>
        /// <param name="elementName">The element tag</param>
        /// <param name="name">The value stored in the child <code> <Name> ... </Name> </code></param>
        /// <returns>The new xml element node item</returns>
        private XmlNode AddCompNode(XmlNode parentNode, string elementName, string name)
        {
            XmlNode newNode = parentNode.AppendChild(parentNode.OwnerDocument.CreateElement(elementName));
            XmlNode newNameNode = newNode.AppendChild(parentNode.OwnerDocument.CreateElement("Name"));
            newNameNode.InnerText = name;

            return newNode;
        }

        /// <summary>
        /// Adds a new DataStore component.
        /// </summary>
        /// <param name="newSim">The new simulation xml node.</param>
        private void AddDataStore(XmlNode newSim)
        {
            this.AddCompNode(newSim, "DataStore", "DataStore");
        }

        /// <summary>
        /// Get the array of doubles for nodePath element
        /// </summary>
        /// <param name="parentNode">Parent node to search</param>
        /// <param name="nodePath">The child node being sought</param>
        /// <param name="defValue">Use this as the default if an invalid value is found</param>
        /// <returns>The array of doubles</returns>
        private double[] GetChildDoubles(XmlNode parentNode, string nodePath, double defValue)
        {
            double[] values = null;

            XmlNode srcNode = XmlUtilities.Find(parentNode, nodePath);
            if (srcNode != null)
            {
                List<XmlNode> nodes = XmlUtilities.ChildNodesByName(srcNode, "double");
                values = new double[nodes.Count];

                for (int i = 0; i < nodes.Count; i++)
                {
                    XmlNode child = nodes[i];
                    if (child.InnerText.Length > 0)
                        values[i] = Convert.ToDouble(child.InnerText, CultureInfo.InvariantCulture);
                    else
                        values[i] = defValue;
                }
            }

            return values;
        }

        /// <summary>
        /// Get the floating point value from the InnerText for the child node of parentNode.
        /// </summary>
        /// <param name="parentNode">Parent node to search</param>
        /// <param name="nodePath">The child node being sought</param>
        /// <param name="defValue">Use this default if an invalid value found</param>
        /// <returns>The floating point value or the default value</returns>
        private double GetChildDouble(XmlNode parentNode, string nodePath, double defValue)
        {
            XmlNode srcNode = XmlUtilities.Find(parentNode, nodePath);
            if ((srcNode != null) && (srcNode.InnerText.Length > 0))
                return Convert.ToDouble(srcNode.InnerText, CultureInfo.InvariantCulture);
            else
                return defValue;
        }

        /// <summary>
        /// Get the inner text for the node
        /// </summary>
        /// <param name="parentNode">Parent node to search</param>
        /// <param name="nodePath">The child node being sought</param>
        /// <returns>The inner text for the node</returns>
        private string GetInnerText(XmlNode parentNode, string nodePath)
        {
            XmlNode srcNode = XmlUtilities.Find(parentNode, nodePath);
            if (srcNode != null)
                return srcNode.InnerText;
            else
                return string.Empty;
        }

        /// <summary>
        /// Get the text for the attribute
        /// </summary>
        /// <param name="node">Xml node</param>
        /// <param name="attr">Attribute name</param>
        /// <returns>The value from the attribute</returns>
        private string AttributeText(XmlNode node, string attr)
        {
            if (node.Attributes[attr] != null)
                return node.Attributes[attr].Value;
            else
                return string.Empty;
        }
    }
}
