﻿using Bridge.Contract;
using Object.Net.Utilities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Bridge.Translator
{
    public class EmitBlock : AbstractEmitterBlock
    {
        public EmitBlock(IEmitter emitter)
        {
            this.Emitter = emitter;
        }

        private void InitEmitter()
        {
            this.Emitter.Output = new StringBuilder();
            this.Emitter.Locals = null;
            this.Emitter.LocalsStack = null;
            this.Emitter.IteratorCount = 0;
            this.Emitter.ThisRefCounter = 0;
            this.Emitter.Writers = new Stack<Tuple<string, StringBuilder, bool, Action>>();
            this.Emitter.IsAssignment = false;
            this.Emitter.Level = 0;
            this.Emitter.IsNewLine = true;
            this.Emitter.EnableSemicolon = true;
            this.Emitter.Comma = false;
            this.Emitter.CurrentDependencies = new List<IPluginDependency>();
        }

        protected virtual StringBuilder GetOutputForType(ITypeInfo typeInfo)
        {
            string module = null;

            if (typeInfo.Module != null)
            {
                module = typeInfo.Module;
            }
            else if (this.Emitter.AssemblyInfo.Module != null)
            {
                module = this.Emitter.AssemblyInfo.Module;
            }

            var fileName = typeInfo.FileName;

            if (fileName.IsEmpty() && this.Emitter.AssemblyInfo.OutputBy != OutputBy.Project)
            {
                switch (this.Emitter.AssemblyInfo.OutputBy)
                {
                    case OutputBy.ClassPath:
                        fileName = typeInfo.FullName;
                        break;
                    case OutputBy.Class:
                        fileName = this.GetIteractiveClassPath(typeInfo);
                        break;
                    case OutputBy.Module:
                        fileName = module;
                        break;
                    case OutputBy.NamespacePath:
                    case OutputBy.Namespace:
                        fileName = typeInfo.Namespace;
                        break;
                    default:
                        break;
                }

                var isPathRelated = this.Emitter.AssemblyInfo.OutputBy == OutputBy.ClassPath ||
                                    this.Emitter.AssemblyInfo.OutputBy == OutputBy.NamespacePath;

                if (fileName.IsNotEmpty() && isPathRelated)
                {
                    fileName = fileName.Replace('.', System.IO.Path.DirectorySeparatorChar);

                    if (this.Emitter.AssemblyInfo.StartIndexInName > 0)
                    {
                        fileName = fileName.Substring(this.Emitter.AssemblyInfo.StartIndexInName);
                    }
                }
            }

            if (fileName.IsEmpty() && this.Emitter.AssemblyInfo.FileName != null)
            {
                fileName = this.Emitter.AssemblyInfo.FileName;
            }

            if (fileName.IsEmpty())
            {
                fileName = AssemblyInfo.DEFAULT_FILENAME;
            }

            // Apply lowerCamelCase to filename if set up in bridge.json (or left default)
            if (this.Emitter.AssemblyInfo.FileNameCasing == FileNameCaseConvert.CamelCase)
            {
                var sepList = new string[] { ".", System.IO.Path.DirectorySeparatorChar.ToString(), "\\", "/" };

                // Populate list only with needed separators, as usually we will never have all four of them
                var neededSepList = new List<string>();

                foreach (var separator in sepList)
                {
                    if (fileName.Contains(separator.ToString()) && !neededSepList.Contains(separator))
                    {
                        neededSepList.Add(separator);
                    }
                }

                // now, separating the filename string only by the used separators, apply lowerCamelCase
                if (neededSepList.Count > 0)
                {
                    foreach (var separator in neededSepList)
                    {
                        var stringList = new List<string>();

                        foreach (var str in fileName.Split(separator[0]))
                        {
                            stringList.Add(str.ToLowerCamelCase());
                        }

                        fileName = stringList.Join(separator);
                    }
                }
                else
                {
                    fileName = fileName.ToLowerCamelCase();
                }
            }

            // Append '.js' extension to file name at translator.Outputs level: this aids in code grouping on files
            // when filesystem is not case sensitive.
            if (!fileName.ToLower().EndsWith("." + Bridge.Translator.AssemblyInfo.JAVASCRIPT_EXTENSION))
            {
                fileName += "." + Bridge.Translator.AssemblyInfo.JAVASCRIPT_EXTENSION;
            }

            switch (this.Emitter.AssemblyInfo.FileNameCasing)
            {
                case FileNameCaseConvert.Lowercase:
                    fileName = fileName.ToLower();
                    break;
                default:
                    var lcFileName = fileName.ToLower();

                    // Find a file name that matches (case-insensitive) and use it as file name (if found)
                    // The used file name will use the same casing of the existing one.
                    foreach (var existingFile in this.Emitter.Outputs.Keys)
                    {
                        if (lcFileName == existingFile.ToLower())
                        {
                            fileName = existingFile;
                        }
                    }

                    break;
            }

            IEmitterOutput output = null;

            if (this.Emitter.Outputs.ContainsKey(fileName))
            {
                output = this.Emitter.Outputs[fileName];
            }
            else
            {
                output = new EmitterOutput(fileName);
                this.Emitter.Outputs.Add(fileName, output);
            }

            if (module == null)
            {
                return output.NonModuletOutput;
            }

            if (module == "")
            {
                module = Bridge.Translator.AssemblyInfo.DEFAULT_FILENAME;
            }

            if (output.ModuleOutput.ContainsKey(module))
            {
                this.Emitter.CurrentDependencies = output.ModuleDependencies[module];
                return output.ModuleOutput[module];
            }

            StringBuilder moduleOutput = new StringBuilder();
            output.ModuleOutput.Add(module, moduleOutput);
            var dependencies = new List<IPluginDependency>();
            output.ModuleDependencies.Add(module, dependencies);

            if (typeInfo.Dependencies.Count > 0)
            {
                dependencies.AddRange(typeInfo.Dependencies);
            }

            this.Emitter.CurrentDependencies = dependencies;

            return moduleOutput;
        }

        /// <summary>
        /// Gets class path iterating until its root class, writing something like this on a 3-level nested class:
        /// RootClass.Lv1ParentClass.Lv2ParentClass.Lv3NestedClass
        /// </summary>
        /// <param name="typeInfo"></param>
        /// <returns></returns>
        private string GetIteractiveClassPath(ITypeInfo typeInfo)
        {
            var fullClassName = typeInfo.Name;
            var maxIterations = 100;
            var curIterations = 0;
            var parent = typeInfo.ParentType;

            while (parent != null && curIterations++ < maxIterations)
            {
                fullClassName = parent.Name + "." + fullClassName;
                parent = parent.ParentType;
            }

            // This should never happen but, just to be sure...
            if (curIterations >= maxIterations)
            {
                throw new ArgumentOutOfRangeException("Iteration count for class '" + typeInfo.FullName + "' exceeded " +
                    maxIterations + " depth iterations until root class!");
            }

            return fullClassName;
        }

        public override void Emit()
        {
            this.Emitter.Writers = new Stack<Tuple<string, StringBuilder, bool, Action>>();
            this.Emitter.Outputs = new EmitterOutputs();

            foreach (var type in this.Emitter.Types)
            {
                if (type.IsObjectLiteral)
                {
                    continue;
                }

                this.InitEmitter();

                ITypeInfo typeInfo;

                if (this.Emitter.TypeInfoDefinitions.ContainsKey(type.GenericFullName))
                {
                    typeInfo = this.Emitter.TypeInfoDefinitions[type.GenericFullName];

                    type.Module = typeInfo.Module;
                    type.FileName = typeInfo.FileName;
                    type.Dependencies = type.Dependencies;
                    typeInfo = type;
                }
                else
                {
                    typeInfo = type;
                }

                this.Emitter.Output = this.GetOutputForType(typeInfo);
                this.Emitter.TypeInfo = type;

                if (this.Emitter.TypeInfo.Module != null)
                {
                    this.Indent();
                }

                new ClassBlock(this.Emitter, this.Emitter.TypeInfo).Emit();
            }

            this.RemovePenultimateEmptyLines(true);
        }
    }
}
