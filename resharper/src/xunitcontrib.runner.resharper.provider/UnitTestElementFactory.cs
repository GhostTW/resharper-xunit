using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Caches;
using JetBrains.ReSharper.UnitTestFramework;
using JetBrains.ReSharper.UnitTestFramework.Elements;
using Xunit.Sdk;

namespace XunitContrib.Runner.ReSharper.UnitTestProvider
{
    [SolutionComponent]
    public class UnitTestElementFactory
    {
        private readonly XunitTestProvider provider;
        private readonly UnitTestElementManager unitTestManager;
        private readonly CacheManager cacheManager;
        private readonly PsiModuleManager psiModuleManager;

        public UnitTestElementFactory(XunitTestProvider provider, UnitTestElementManager unitTestManager, CacheManager cacheManager, PsiModuleManager psiModuleManager)
        {
            this.provider = provider;
            this.unitTestManager = unitTestManager;
            this.cacheManager = cacheManager;
            this.psiModuleManager = psiModuleManager;
        }

        public XunitTestClassElement GetOrCreateTestClass(IProject project, IClrTypeName typeName, string assemblyLocation)
        {
            var id = "xunit:" + typeName.FullName;
            var element = unitTestManager.GetElementById(project, id);
            if (element != null)
            {
                element.State = UnitTestElementState.Valid;
                var classElement = element as XunitTestClassElement;
                if (classElement != null)   // Shouldn't be null, unless someone else has the same id
                    classElement.AssemblyLocation = assemblyLocation;   // In case it's changed, e.g. someone's switched from Debug to Release
                return classElement;
            }

            return new XunitTestClassElement(provider, new ProjectModelElementEnvoy(project), cacheManager, psiModuleManager, id, typeName.GetPersistent(), assemblyLocation);
        }

        public XunitTestMethodElement GetOrCreateTestMethod(IProject project, XunitTestClassElement testClassElement, IClrTypeName typeName, string methodName, string skipReason, MultiValueDictionary<string, string> traits)
        {
            var baseTypeName = string.Empty;
            if (!testClassElement.TypeName.Equals(typeName))
                baseTypeName = typeName.ShortName + ".";

            var id = string.Format("xunit:{0}.{1}{2}", testClassElement.TypeName.FullName, baseTypeName, methodName);
            var element = unitTestManager.GetElementById(project, id);
            if (element != null)
            {
                element.State = UnitTestElementState.Valid;
                var methodElement = element as XunitTestMethodElement;
                if (methodElement != null)
                    methodElement.SetCategories(GetCategories(traits));
                return element as XunitTestMethodElement;
            }

            var categories = GetCategories(traits);
            return new XunitTestMethodElement(provider, testClassElement, new ProjectModelElementEnvoy(project), cacheManager, psiModuleManager, id, typeName.GetPersistent(), methodName, skipReason, categories);
        }

        private static IEnumerable<string> GetCategories(MultiValueDictionary<string, string> traits)
        {
            return from key in traits
                   where key.Trim() != string.Empty
                   from value in traits[key]
                   where value.Trim() != string.Empty
                   select string.Compare(key, "category", StringComparison.InvariantCultureIgnoreCase) != 0
                              ? string.Format("{0}[{1}]", key.Trim(), value.Trim())
                              : value;
        }

        public XunitInheritedTestMethodContainerElement GetOrCreateInheritedTestMethodContainer(IProject project, IClrTypeName typeName, string methodName)
        {
            var id = string.Format("xunit:{0}.{1}", typeName.FullName, methodName);
            var element = unitTestManager.GetElementById(project, id);
            if (element != null)
                return element as XunitInheritedTestMethodContainerElement;

            return new XunitInheritedTestMethodContainerElement(provider, project, id, typeName.GetPersistent(), methodName);
        }

        public static XunitTestTheoryElement GetTestTheory(IProject project, XunitTestMethodElement methodElement, string name)
        {
            var id = GetTestTheoryId(methodElement, GetTestTheoryShortName(name, methodElement));
            var unitTestElementManager = project.GetSolution().GetComponent<IUnitTestElementManager>();
            return unitTestElementManager.GetElementById(project, id) as XunitTestTheoryElement;
        }

        public static XunitTestTheoryElement CreateTestTheory(IUnitTestProvider provider, IProject project, XunitTestMethodElement methodElement, string name)
        {
            var shortName = GetTestTheoryShortName(name, methodElement);
            var id = GetTestTheoryId(methodElement, shortName);
            return new XunitTestTheoryElement(provider, methodElement, new ProjectModelElementEnvoy(project), id, shortName);
        }

        public XunitTestTheoryElement GetOrCreateTestTheory(IProject project, XunitTestMethodElement methodElement, string name)
        {
            var element = GetTestTheory(project, methodElement, name);
            return element ?? CreateTestTheory(provider, project, methodElement, name);
        }

        private static string GetTestTheoryShortName(string theoryName, XunitTestMethodElement methodElement)
        {
            var prefix = methodElement.TypeName.FullName + ".";
            return theoryName.StartsWith(prefix) ? theoryName.Substring(prefix.Length) : theoryName;
        }

        private static string GetTestTheoryId(XunitTestMethodElement methodElement, string shortName)
        {
            return string.Format("{0}.{1}", methodElement.Id, shortName);
        }
    }
}