using System;
using System.Collections.Generic;
using JetBrains.Application;
using JetBrains.Application.Progress;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.ReSharper.UnitTestFramework;
using JetBrains.Util;
using Xunit.Sdk;
using System.Linq;

namespace XunitContrib.Runner.ReSharper.UnitTestProvider
{
    public class XunitPsiFileExplorer : IRecursiveElementProcessor
    {
        private readonly UnitTestElementFactory unitTestElementFactory;
        private readonly UnitTestElementLocationConsumer consumer;
        private readonly IFile file;
        private readonly CheckForInterrupt interrupted;
        private readonly IProject project;
        private readonly string assemblyPath;
        private readonly Dictionary<ITypeElement, XunitTestClassElement> classes = new Dictionary<ITypeElement, XunitTestClassElement>();
        private readonly IProjectFile projectFile;

        // TODO: The nunit code uses UnitTestAttributeCache
        public XunitPsiFileExplorer(XunitTestProvider provider, UnitTestElementFactory unitTestElementFactory, UnitTestElementLocationConsumer consumer, IFile file, CheckForInterrupt interrupted)
        {
            if (file == null)
                throw new ArgumentNullException("file");

            if (provider == null)
                throw new ArgumentNullException("provider");

            this.consumer = consumer;
            this.unitTestElementFactory = unitTestElementFactory;
            this.file = file;
            this.interrupted = interrupted;
            projectFile = file.GetSourceFile().ToProjectFile();
            project = file.GetProject();

            assemblyPath = UnitTestManager.GetOutputAssemblyPath(project).FullPath;
        }

        public bool ProcessingIsFinished
        {
            get
            {
                if (interrupted())
                    throw new ProcessCancelledException();

                return false;
            }
        }

        public bool InteriorShouldBeProcessed(ITreeNode element)
        {
            if (element is ITypeMemberDeclaration)
                return (element is ITypeDeclaration);

            return true;
        }

        public void ProcessBeforeInterior(ITreeNode element)
        {
            var declaration = element as IDeclaration;

            if (declaration != null)
            {
                IUnitTestElement testElement = null;
                var declaredElement = declaration.DeclaredElement;

                var testClass = declaredElement as IClass;
                if (testClass != null)
                    testElement = ProcessTestClass(testClass);

                var testMethod = declaredElement as IMethod;
                if (testMethod != null)
                    testElement = ProcessTestMethod(testMethod) ?? testElement;

                if (testElement != null)
                {
                    // Ensure that the method has been implemented, i.e. it has a name and a document
                    var nameRange = declaration.GetNameDocumentRange().TextRange;
                    var documentRange = declaration.GetDocumentRange();
                    if (nameRange.IsValid && documentRange.IsValid())
                    {
                        var disposition = new UnitTestElementDisposition(testElement, file.GetSourceFile().ToProjectFile(),
                                                                         nameRange, documentRange.TextRange);
                        consumer(disposition);
                    }
                }
            }
        }

        public void ProcessAfterInterior(ITreeNode element)
        {
            var declaration = element as IDeclaration;

            if (declaration != null)
            {
                var declaredElement = declaration.DeclaredElement;

                var testClass = declaredElement as IClass;
                XunitTestClassElement testElement;
                if (testClass != null && classes.TryGetValue(testClass, out testElement))
                {
                    foreach (var unitTestElement in testElement.Children.Where(x => x.State == UnitTestElementState.Pending))
                    {
                        unitTestElement.State = UnitTestElementState.Invalid;
                    }
                }
            }
        }

        private IUnitTestElement ProcessTestClass(IClass testClass)
        {
            if (!IsValidTestClass(testClass))
                return null;

            XunitTestClassElement testElement;

            if (!classes.TryGetValue(testClass, out testElement))
            {
                var clrTypeName = testClass.GetClrName();
                testElement = unitTestElementFactory.GetOrCreateTestClass(project, clrTypeName, assemblyPath);

                foreach (var testMethod in IsInThisFile(testElement.Children))
                    testMethod.State = UnitTestElementState.Pending;
                classes.Add(testClass, testElement);
            }

            return testElement;
        }

        private IEnumerable<IUnitTestElement> IsInThisFile(IEnumerable<IUnitTestElement> unitTestElements)
        {
            return from element in unitTestElements
                   let projectFiles = element.GetProjectFiles()
                   where projectFiles == null || projectFiles.IsEmpty() || projectFiles.Contains(projectFile)
                   select element;
        }

        private static bool IsValidTestClass(IClass testClass)
        {
            return testClass.IsUnitTestContainer() && !HasUnsupportedRunWith(testClass.AsTypeInfo());
        }

        private static bool HasUnsupportedRunWith(ITypeInfo typeInfo)
        {
            return TypeUtility.HasRunWith(typeInfo);
        }

        private IUnitTestElement ProcessTestMethod(IMethod method)
        {
            var type = method.GetContainingType();
            var @class = type as IClass;
            if (type == null || @class == null || !IsValidTestClass(@class))
                return null;

            var command = TestClassCommandFactory.Make(@class.AsTypeInfo());
            if (command == null)
                return null;

            var testClassElement = classes[type];
            if (testClassElement == null)
                return null;

            var methodInfo = method.AsMethodInfo();
            if (command.IsTestMethod(methodInfo))
            {
                var clrTypeName = type.GetClrName();
                return unitTestElementFactory.GetOrCreateTestMethod(project, testClassElement, clrTypeName, method.ShortName, MethodUtility.GetSkipReason(methodInfo));
            }

            return null;
        }
    }
}
