#region License

// SLNTools
// Copyright (c) 2009
// by Christian Warren
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
// documentation files (the "Software"), to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and
// to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or substantial portions
// of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED
// TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
// CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;

namespace CWDev.SLNTools.Core
{
    using Merge;

    public class Project
    {
        private readonly SolutionFile r_container;
        private readonly string r_projectGuid;
        private string m_projectTypeGuid;
        private string m_projectName;
        private string m_relativePath;
        private string m_parentFolderGuid;
        private readonly SectionHashList r_projectSections;
        private readonly PropertyLineHashList r_versionControlLines;
        private readonly PropertyLineHashList r_projectConfigurationPlatformsLines;

        public Project(SolutionFile container, Project original)
            : this(
                    container,
                    original.ProjectGuid,
                    original.ProjectTypeGuid,
                    original.ProjectName,
                    original.RelativePath,
                    original.ParentFolderGuid,
                    original.ProjectSections,
                    original.VersionControlLines,
                    original.ProjectConfigurationPlatformsLines)
        {
        }

        public Project(
                    SolutionFile container,
                    string projectGuid,
                    string projectTypeGuid,
                    string projectName,
                    string relativePath,
                    string parentFolderGuid,
                    IEnumerable<Section> projectSections,
                    IEnumerable<PropertyLine> versionControlLines,
                    IEnumerable<PropertyLine> projectConfigurationPlatformsLines)
        {
            r_container = container;
            r_projectGuid = projectGuid;
            m_projectTypeGuid = projectTypeGuid;
            m_projectName = projectName;
            m_relativePath = relativePath;
            m_parentFolderGuid = parentFolderGuid;
            r_projectSections = new SectionHashList(projectSections);
            r_versionControlLines = new PropertyLineHashList(versionControlLines);
            r_projectConfigurationPlatformsLines = new PropertyLineHashList(projectConfigurationPlatformsLines);
        }

        public string ProjectGuid
        {
            get { return r_projectGuid; }
        }
        public string ProjectTypeGuid
        {
            get { return m_projectTypeGuid; }
            set { m_projectTypeGuid = value; }
        }
        public string ProjectName
        {
            get { return m_projectName; }
            set { m_projectName = value; }
        }
        public string RelativePath
        {
            get { return m_relativePath; }
            set { m_relativePath = value; }
        }
        public string FullPath
        {
            get { return Environment.ExpandEnvironmentVariables(Path.Combine(Path.GetDirectoryName(r_container.SolutionFullPath), m_relativePath)); }
        }
        public string ParentFolderGuid
        {
            get { return m_parentFolderGuid; }
            set { m_parentFolderGuid = value; }
        }
        public SectionHashList ProjectSections
        {
            get { return r_projectSections; }
        }
        public PropertyLineHashList VersionControlLines
        {
            get { return r_versionControlLines; }
        }
        public PropertyLineHashList ProjectConfigurationPlatformsLines
        {
            get { return r_projectConfigurationPlatformsLines; }
        }

        public Project ParentFolder
        {
            get
            {
                if (m_parentFolderGuid == null)
                    return null;

                return FindProjectInContainer(
                            m_parentFolderGuid,
                            "Cannot find the parent folder of project '{0}'. \nProject guid: {1}\nParent folder guid: {2}",
                            m_projectName,
                            r_projectGuid,
                            m_parentFolderGuid);
            }
        }
        public string ProjectFullName
        {
            get
            {
                if (this.ParentFolder != null)
                {
                    return this.ParentFolder.ProjectFullName + @"\" + this.ProjectName;
                }
                else
                {
                    return this.ProjectName;
                }
            }
        }
        public IEnumerable<Project> Childs
        {
            get
            {
                if (m_projectTypeGuid == KnownProjectTypeGuid.SolutionFolder)
                {
                    foreach (Project project in r_container.Projects)
                    {
                        if (project.m_parentFolderGuid == r_projectGuid)
                            yield return project;
                    }
                }
            }
        }
        public IEnumerable<Project> AllDescendants
        {
            get
            {
                foreach (var child in this.Childs)
                {
                    yield return child;
                    foreach (var subchild in child.AllDescendants)
                    {
                        yield return subchild;
                    }
                }
            }
        }
		public IEnumerable<ReferencedAssembly> References
		{
			get
			{
				switch (m_projectTypeGuid)
				{
					case KnownProjectTypeGuid.CSharp:
					case KnownProjectTypeGuid.FSharp:
						if (!File.Exists(this.FullPath))
						{
							throw new SolutionFileException(string.Format(
										"Cannot detect references of project '{0}' because the project file cannot be found.\nProject full path: '{1}'",
										m_projectName,
										this.FullPath));
						}
						var docManaged = new XmlDocument();
						docManaged.Load(this.FullPath);

						var xmlManager = new XmlNamespaceManager(docManaged.NameTable);
						xmlManager.AddNamespace("prefix", "http://schemas.microsoft.com/developer/msbuild/2003");

						foreach (XmlNode xmlNode in docManaged.SelectNodes(@"//prefix:Reference", xmlManager))
						{
							string referenceInclude = xmlNode.Attributes.GetNamedItem("Include").InnerText;
							string referencePackage = xmlNode.SelectSingleNode(@"prefix:Package", xmlManager)?.InnerText.Trim(); // TODO handle null
							yield return new ReferencedAssembly(
										referenceInclude,
										referencePackage);
						}
						break;
					default:
						break;
				}
			}
		}

		public IEnumerable<Project> Dependencies
        {
            get
            {
                if (this.ParentFolder != null)
                {
                    yield return this.ParentFolder;
                }

                if (r_projectSections.Contains("ProjectDependencies"))
                {
                    foreach (var propertyLine in r_projectSections["ProjectDependencies"].PropertyLines)
                    {
                        var dependencyGuid = propertyLine.Name;
                        yield return FindProjectInContainer(
                                    dependencyGuid,
                                    "Cannot find one of the dependency of project '{0}'.\nProject guid: {1}\nDependency guid: {2}\nReference found in: ProjectDependencies section of the solution file",
                                    m_projectName,
                                    r_projectGuid,
                                    dependencyGuid);
                    }
                }

                switch (m_projectTypeGuid)
                {
                    case KnownProjectTypeGuid.VisualBasic:
                    case KnownProjectTypeGuid.CSharp:
                    case KnownProjectTypeGuid.JSharp:
                    case KnownProjectTypeGuid.FSharp:
                    default:
                        if (! File.Exists(this.FullPath))
                        {
                            throw new SolutionFileException(string.Format(
                                        "Cannot detect dependencies of project '{0}' because the project file cannot be found.\nProject full path: '{1}'",
                                        m_projectName,
                                        this.FullPath));
                        }

                        var docManaged = new XmlDocument();
                        docManaged.Load(this.FullPath);

                        var xmlManager = new XmlNamespaceManager(docManaged.NameTable);
                        xmlManager.AddNamespace("prefix", "http://schemas.microsoft.com/developer/msbuild/2003");

                        foreach (XmlNode xmlNode in docManaged.SelectNodes(@"//prefix:ProjectReference", xmlManager))
                        {
                            string dependencyGuid = xmlNode.SelectSingleNode(@"prefix:Project", xmlManager).InnerText.Trim(); // TODO handle null
                            string dependencyName = xmlNode.SelectSingleNode(@"prefix:Name", xmlManager).InnerText.Trim(); // TODO handle null
                            yield return FindProjectInContainer(
                                        dependencyGuid,
                                        "Cannot find one of the dependency of project '{0}'.\nProject guid: {1}\nDependency guid: {2}\nDependency name: {3}\nReference found in: ProjectReference node of file '{4}'",
                                        m_projectName,
                                        r_projectGuid,
                                        dependencyGuid,
                                        dependencyName,
                                        this.FullPath);
                        }
                        break;

                    case KnownProjectTypeGuid.SolutionFolder:
                        break;

                    case KnownProjectTypeGuid.VisualC:
                        if (!File.Exists(this.FullPath))
                        {
                            throw new SolutionFileException(string.Format(
                                        "Cannot detect dependencies of projet '{0}' because the project file cannot be found.\nProject full path: '{1}'",
                                        m_projectName,
                                        this.FullPath));
                        }

                        var docVisualC = new XmlDocument();
                        docVisualC.Load(this.FullPath);

                        foreach (XmlNode xmlNode in docVisualC.SelectNodes(@"//ProjectReference"))
                        {
                            var dependencyGuid = xmlNode.Attributes["ReferencedProjectIdentifier"].Value; // TODO handle null
                            string dependencyRelativePathToProject;
                            XmlNode relativePathToProjectNode = xmlNode.Attributes["RelativePathToProject"];
                            if (relativePathToProjectNode != null)
                            {
                                dependencyRelativePathToProject = relativePathToProjectNode.Value;
                            }
                            else
                            {
                                dependencyRelativePathToProject = "???";
                            }
                            yield return FindProjectInContainer(
                                        dependencyGuid,
                                        "Cannot find one of the dependency of project '{0}'.\nProject guid: {1}\nDependency guid: {2}\nDependency relative path: '{3}'\nReference found in: ProjectReference node of file '{4}'",
                                        m_projectName,
                                        r_projectGuid,
                                        dependencyGuid,
                                        dependencyRelativePathToProject,
                                        this.FullPath);
                        }
                        break;

                    case KnownProjectTypeGuid.Setup:
                        if (!File.Exists(this.FullPath))
                        {
                            throw new SolutionFileException(string.Format(
                                        "Cannot detect dependencies of projet '{0}' because the project file cannot be found.\nProject full path: '{1}'",
                                        m_projectName,
                                        this.FullPath));
                        }

                        foreach (string line in File.ReadAllLines(this.FullPath))
                        {
                            var regex = new Regex("^\\s*\"OutputProjectGuid\" = \"\\d*\\:(?<PROJECTGUID>.*)\"$");
                            var match = regex.Match(line);
                            if (match.Success)
                            {
                                var dependencyGuid = match.Groups["PROJECTGUID"].Value.Trim();
                                yield return FindProjectInContainer(
                                            dependencyGuid,
                                            "Cannot find one of the dependency of project '{0}'.\nProject guid: {1}\nDependency guid: {2}\nReference found in: OutputProjectGuid line of file '{3}'",
                                            m_projectName,
                                            r_projectGuid,
                                            dependencyGuid,
                                            this.FullPath);
                            }
                        }
                        break;

                    case KnownProjectTypeGuid.WebProject:
                        // Format is: "({GUID}|ProjectName;)*"
                        // Example: "{GUID}|Infra.dll;{GUID2}|Services.dll;"
                        var propertyLine = r_projectSections["WebsiteProperties"].PropertyLines["ProjectReferences"];
                        var value = propertyLine.Value;
                        if (value.StartsWith("\""))
                            value = value.Substring(1);
                        if (value.EndsWith("\""))
                            value = value.Substring(0, value.Length - 1);

                        foreach (string dependency in value.Split(';'))
                        {
                            if (dependency.Trim().Length > 0)
                            {
                                var parts = dependency.Split('|');
                                var dependencyGuid = parts[0];
                                var dependencyName = parts[1]; // TODO handle null
                                yield return FindProjectInContainer(
                                            dependencyGuid,
                                            "Cannot find one of the dependency of project '{0}'.\nProject guid: {1}\nDependency guid: {2}\nDependency name: {3}\nReference found in: ProjectReferences line in WebsiteProperties section of the solution file",
                                            m_projectName,
                                            r_projectGuid,
                                            dependencyGuid,
                                            dependencyName);
                            }
                        }
                        break;
                }
            }
        }

        public override string ToString()
        {
            return string.Format("Project '{0}'", this.ProjectFullName);
        }

        private Project FindProjectInContainer(string projectGuid, string errorMessageFormat, params object[] errorMessageParams)
        {
            var project = r_container.Projects.FindByGuid(projectGuid);
            if (project == null)
            {
                throw new SolutionFileException(string.Format(errorMessageFormat, errorMessageParams));
            }
            return project;
        }

        #region public: Methods ToElement / FromElement

        private const string TagProjectTypeGuid = "ProjectTypeGuid";
        private const string TagProjectName = "ProjectName";
        private const string TagRelativePath = "RelativePath";
        private const string TagParentFolder = "ParentFolder";
        private const string TagProjectSection = "S_";
        private const string TagVersionControlLines = "VCL_";
        private const string TagProjectConfigurationPlatformsLines = "PCPL_";

        public NodeElement ToElement(ElementIdentifier identifier)
        {
            var childs = new List<Element>
                {
                    new ValueElement(new ElementIdentifier(TagProjectTypeGuid), this.ProjectTypeGuid),
                    new ValueElement(new ElementIdentifier(TagProjectName), this.ProjectName),
                    new ValueElement(new ElementIdentifier(TagRelativePath), this.RelativePath)
                };
            if (this.ParentFolder != null)
            {
                childs.Add(new ValueElement(
                            new ElementIdentifier(TagParentFolder),
                            this.ParentFolder.ProjectFullName));
            }

            foreach (var projectSection in this.ProjectSections)
            {
                childs.Add(
                            projectSection.ToElement(
                                new ElementIdentifier(
                                    TagProjectSection + projectSection.Name,
                                    string.Format("{0} \"{1}\"", projectSection.SectionType, projectSection.Name))));
            }
            foreach (var propertyLine in this.VersionControlLines)
            {
                childs.Add(
                            new ValueElement(
                                new ElementIdentifier(
                                    TagVersionControlLines + propertyLine.Name,
                                    @"VersionControlLine\" + propertyLine.Name),
                                propertyLine.Value));
            }
            foreach (var propertyLine in this.ProjectConfigurationPlatformsLines)
            {
                childs.Add(
                            new ValueElement(
                                new ElementIdentifier(
                                    TagProjectConfigurationPlatformsLines + propertyLine.Name,
                                    @"ProjectConfigurationPlatformsLine\" + propertyLine.Name),
                                propertyLine.Value));
            }
            return new NodeElement(
                            identifier,
                            childs);
        }

        public static Project FromElement(string projectGuid, NodeElement element, Dictionary<string, string> solutionFolderGuids)
        {
            string projectTypeGuid = null;
            string projectName = null;
            string relativePath = null;
            string parentFolderGuid = null;
            var projectSections = new List<Section>();
            var versionControlLines = new List<PropertyLine>();
            var projectConfigurationPlatformsLines = new List<PropertyLine>();

            foreach (var child in element.Childs)
            {
                var identifier = child.Identifier;
                if (identifier.Name == TagProjectTypeGuid)
                {
                    projectTypeGuid = ((ValueElement)child).Value;
                }
                else if (identifier.Name == TagProjectName)
                {
                    projectName = ((ValueElement)child).Value;
                }
                else if (identifier.Name == TagRelativePath)
                {
                    relativePath = ((ValueElement)child).Value;
                }
                else if (identifier.Name == TagParentFolder)
                {
                    var parentProjectFullName = ((ValueElement)child).Value;
                    if (! solutionFolderGuids.ContainsKey(parentProjectFullName))
                    {
                        throw new Exception("TODO");
                    }

                    parentFolderGuid = solutionFolderGuids[parentProjectFullName];
                }
                else if (identifier.Name.StartsWith(TagProjectSection))
                {
                    var sectionName = identifier.Name.Substring(TagProjectSection.Length);
                    projectSections.Add(
                                Section.FromElement(
                                    sectionName,
                                    (NodeElement)child));
                }
                else if (identifier.Name.StartsWith(TagVersionControlLines))
                {
                    var name = identifier.Name.Substring(TagVersionControlLines.Length);
                    var value = ((ValueElement)child).Value;
                    versionControlLines.Add(new PropertyLine(name, value));
                }
                else if (identifier.Name.StartsWith(TagProjectConfigurationPlatformsLines))
                {
                    var name = identifier.Name.Substring(TagProjectConfigurationPlatformsLines.Length);
                    var value = ((ValueElement)child).Value;
                    projectConfigurationPlatformsLines.Add(new PropertyLine(name, value));
                }
                else
                {
                    throw new SolutionFileException(string.Format("Invalid identifier '{0}'.", identifier.Name));
                }
            }

            if (projectTypeGuid == null)
                throw new SolutionFileException(string.Format("Missing subelement '{0}' in a section element.", TagProjectTypeGuid));
            if (projectName == null)
                throw new SolutionFileException(string.Format("Missing subelement '{0}' in a section element.", TagProjectName));
            if (relativePath == null)
                throw new SolutionFileException(string.Format("Missing subelement '{0}' in a section element.", TagRelativePath));

            return new Project(
                        null,
                        projectGuid,
                        projectTypeGuid,
                        projectName,
                        relativePath,
                        parentFolderGuid,
                        projectSections,
                        versionControlLines,
                        projectConfigurationPlatformsLines);
        }

        #endregion
    }
}
