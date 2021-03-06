﻿using ICSharpCode.NRefactory.CSharp;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using ICSharpCode.NRefactory.TypeSystem;
using System.Text;
using Mono.Cecil;
using Object.Net.Utilities;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.NRefactory.TypeSystem.Implementation;
using Bridge.Contract;

namespace Bridge.Translator
{
    public partial class ConstructorBlock : AbstractMethodBlock, IConstructorBlock
    {
        public ConstructorBlock(IEmitter emitter, ITypeInfo typeInfo, bool staticBlock)
        {
            this.Emitter = emitter;
            this.TypeInfo = typeInfo;
            this.StaticBlock = staticBlock;
        }

        public ITypeInfo TypeInfo
        {
            get;
            set;
        }

        public bool StaticBlock
        {
            get;
            set;
        }

        public override void Emit()
        {
            if (this.StaticBlock)
            {
                this.EmitCtorForStaticClass();
            }
            else
            {
                this.EmitCtorForInstantiableClass();
            }
        }

        protected virtual IEnumerable<string> GetInjectors()
        {
            var handlers = this.GetEvents();
            var aspects = this.Emitter.Plugins.GetConstructorInjectors(this);
            return aspects.Concat(handlers);
        }

        protected virtual void EmitCtorForStaticClass()
        {
            var ctor = this.TypeInfo.StaticCtor;

            if (ctor != null && ctor.Body.HasChildren)
            {
                this.ResetLocals();
                var prevNamesMap = this.BuildLocalsNamesMap();
                this.Write("constructor");
                this.WriteColon();
                this.WriteFunction();
                this.WriteOpenCloseParentheses(true);

                this.BeginBlock();

                ctor.Body.AcceptChildren(this.Emitter);

                this.EndBlock();
                this.ClearLocalsNamesMap(prevNamesMap);
                this.Emitter.Comma = true;
            }

            var injectors = this.GetInjectors();
            IEnumerable<string> fieldsInjectors = null;

            var fieldBlock = new FieldBlock(this.Emitter, this.TypeInfo, true, true);
            fieldBlock.Emit();

            fieldsInjectors = fieldBlock.Injectors;

            if (fieldBlock.WasEmitted)
            {
                this.Emitter.Comma = true;
            }

            if (this.TypeInfo.StaticConfig.HasConfigMembers || injectors.Any() || fieldsInjectors.Any())
            {
                this.EnsureComma();

                if (this.TypeInfo.StaticMethods.Any(m => m.Value.Any(subm => this.Emitter.GetEntityName(subm) == "config")) ||
                    this.TypeInfo.StaticConfig.Fields.Any(m => m.GetName(this.Emitter) == "config"))
                {
                    this.Write("$");
                }

                this.Write("config");

                this.WriteColon();
                this.BeginBlock();

                if (this.TypeInfo.StaticConfig.HasConfigMembers)
                {
                    var configBlock = new FieldBlock(this.Emitter, this.TypeInfo, true, false);
                    configBlock.Emit();

                    if (configBlock.Injectors.Count > 0)
                    {
                        injectors = configBlock.Injectors.Concat(injectors);
                    }

                    if (configBlock.WasEmitted)
                    {
                        this.Emitter.Comma = true;
                    }
                }

                if (fieldsInjectors.Any())
                {
                    injectors = fieldsInjectors.Concat(injectors);
                }

                if (injectors.Count() > 0)
                {
                    this.EnsureComma();

                    this.Write("init");
                    this.WriteColon();
                    this.WriteFunction();
                    this.WriteOpenParentheses();
                    this.WriteCloseParentheses();
                    this.WriteSpace();
                    this.BeginBlock();
                    foreach (var fn in injectors)
                    {
                        this.Write(fn);
                        this.WriteNewLine();
                    }
                    this.EndBlock();
                    this.Emitter.Comma = true;
                }
                this.WriteNewLine();
                this.EndBlock();
            }
        }

        protected virtual void EmitInitMembers()
        {
            var injectors = this.GetInjectors();
            IEnumerable<string> fieldsInjectors = null; 

            var fieldBlock = new FieldBlock(this.Emitter, this.TypeInfo, false, true);
            fieldBlock.Emit();

            fieldsInjectors = fieldBlock.Injectors;

            if (fieldBlock.WasEmitted)
            {
                this.Emitter.Comma = true;
            }            

            if (!this.TypeInfo.InstanceConfig.HasConfigMembers && !injectors.Any() && !fieldsInjectors.Any())
            {
                return;
            }

            if (this.TypeInfo.InstanceMethods.Any(m => m.Value.Any(subm => this.Emitter.GetEntityName(subm) == "config")) ||
                this.TypeInfo.InstanceConfig.Fields.Any(m => m.GetName(this.Emitter) == "config"))
            {
                this.Write("$");
            }

            this.EnsureComma();
            this.Write("config");

            this.WriteColon();            
            this.BeginBlock();            

            var changeCase = this.Emitter.ChangeCase;
            var baseType = this.Emitter.GetBaseTypeDefinition();

            if (this.TypeInfo.InstanceConfig.HasConfigMembers)
            {
                var configBlock = new FieldBlock(this.Emitter, this.TypeInfo, false, false);
                configBlock.Emit();

                if (configBlock.Injectors.Count > 0)
                {
                    injectors = configBlock.Injectors.Concat(injectors);
                }

                if (configBlock.WasEmitted)
                {
                    this.Emitter.Comma = true;
                }
            }

            if (fieldsInjectors.Any())
            {
                injectors = fieldsInjectors.Concat(injectors);
            }

            if (injectors.Count() > 0)
            {
                this.EnsureComma();
                this.Write("init");
                this.WriteColon();
                this.WriteFunction();
                this.WriteOpenParentheses();
                this.WriteCloseParentheses();
                this.WriteSpace();
                this.BeginBlock();

                foreach (var fn in injectors)
                {
                    this.Write(fn);
                    this.WriteNewLine();
                }

                this.EndBlock();

                this.Emitter.Comma = true;
            }

            this.WriteNewLine();
            this.EndBlock();

        }

        protected virtual void EmitCtorForInstantiableClass()
        {
            this.EmitInitMembers();

            if (!this.TypeInfo.HasInstantiable)
            {
                return;
            }

            var baseType = this.Emitter.GetBaseTypeDefinition();
            var typeDef = this.Emitter.GetTypeDefinition();

            if (typeDef.IsValueType)
            {
                this.TypeInfo.Ctors.Add(new ConstructorDeclaration
                {
                    Modifiers = Modifiers.Public,
                    Body = new BlockStatement()
                });
            }

            foreach (var ctor in this.TypeInfo.Ctors)
            {
                this.EnsureComma();
                this.ResetLocals();
                var prevMap = this.BuildLocalsMap();
                var prevNamesMap = this.BuildLocalsNamesMap();
                this.AddLocals(ctor.Parameters, ctor.Body);

                var ctorName = "constructor";

                if (this.TypeInfo.Ctors.Count > 1 && ctor.Parameters.Count > 0)
                {
                    var overloads = OverloadsCollection.Create(this.Emitter, ctor);
                    ctorName = overloads.GetOverloadName();
                }

                this.Write(ctorName);

                this.WriteColon();
                this.WriteFunction();

                this.EmitMethodParameters(ctor.Parameters, ctor);

                this.WriteSpace();
                this.BeginBlock();

                var requireNewLine = false;
                var changeCase = this.Emitter.ChangeCase;

                if (baseType != null && (!this.Emitter.Validator.IsIgnoreType(baseType) || this.Emitter.Validator.IsBridgeClass(baseType)) ||
                    (ctor.Initializer != null && ctor.Initializer.ConstructorInitializerType == ConstructorInitializerType.This))
                {
                    if (requireNewLine)
                    {
                        this.WriteNewLine();
                    }
                    this.EmitBaseConstructor(ctor, ctorName);
                    requireNewLine = true;
                }

                var script = this.Emitter.GetScript(ctor);

                if (script == null)
                {
                    if (ctor.Body.HasChildren)
                    {
                        if (requireNewLine)
                        {
                            this.WriteNewLine();
                        }
                        this.ConvertParamsToReferences(ctor.Parameters);
                        ctor.Body.AcceptChildren(this.Emitter);
                    }
                }
                else
                {
                    if (requireNewLine)
                    {
                        this.WriteNewLine();
                    }

                    foreach (var line in script)
                    {
                        this.Write(line);
                        this.WriteNewLine();
                    }
                }

                this.EndBlock();
                this.Emitter.Comma = true;
                this.ClearLocalsMap(prevMap);
                this.ClearLocalsNamesMap(prevNamesMap);
            }
        }

        protected virtual void EmitBaseConstructor(ConstructorDeclaration ctor, string ctorName)
        {
            var initializer = ctor.Initializer != null && !ctor.Initializer.IsNull ? ctor.Initializer : new ConstructorInitializer()
            {
                ConstructorInitializerType = ConstructorInitializerType.Base
            };

            bool appendScope = false;

            if (initializer.ConstructorInitializerType == ConstructorInitializerType.Base)
            {
                var baseType = this.Emitter.GetBaseTypeDefinition();
                var baseName = "constructor";
                if (ctor.Initializer != null && !ctor.Initializer.IsNull)
                {
                    var member = ((InvocationResolveResult)this.Emitter.Resolver.ResolveNode(ctor.Initializer, this.Emitter)).Member;
                    var overloads = OverloadsCollection.Create(this.Emitter, member);
                    if (overloads.HasOverloads)
                    {
                        baseName = overloads.GetOverloadName();
                    }
                }

                if (baseName == "constructor")
                {
                    baseName = "$constructor";
                }

                this.Write(this.Emitter.ShortenTypeName(Helpers.GetScriptFullName(baseType)), ".prototype.");
                this.Write(baseName);
                this.Write(".call");
                appendScope = true;
            }
            else
            {
                this.WriteThis();
                this.WriteDot();

                var baseName = "constructor";
                var member = ((InvocationResolveResult)this.Emitter.Resolver.ResolveNode(ctor.Initializer, this.Emitter)).Member;
                var overloads = OverloadsCollection.Create(this.Emitter, member);
                if (overloads.HasOverloads)
                {
                    baseName = overloads.GetOverloadName();
                }

                if (baseName == "constructor")
                {
                    baseName = "$constructor";
                }

                this.Write(baseName);
            }

            this.WriteOpenParentheses();

            if (appendScope)
            {
                this.WriteThis();

                if (initializer.Arguments.Count > 0)
                {
                    this.WriteComma();
                }
            }

            var args = new List<Expression>(initializer.Arguments);
            for (int i = 0; i < args.Count; i++)
            {
                args[i].AcceptVisitor(this.Emitter);
                if (i != (args.Count - 1))
                {
                    this.WriteComma();
                }
            }

            this.WriteCloseParentheses();
            this.WriteSemiColon();
            this.WriteNewLine();
        }
    }
}
