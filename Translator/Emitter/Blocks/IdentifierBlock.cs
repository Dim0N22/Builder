﻿using Bridge.Contract;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.CSharp.Resolver;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.NRefactory.TypeSystem.Implementation;
using Mono.Cecil;
using System;
using System.Text;

namespace Bridge.Translator
{
    public class IdentifierBlock : ConversionBlock
    {
        public IdentifierBlock(IEmitter emitter, IdentifierExpression identifierExpression)
        {
            this.Emitter = emitter;
            this.IdentifierExpression = identifierExpression;
        }

        public IdentifierExpression IdentifierExpression 
        { 
            get; 
            set; 
        }

        protected override Expression GetExpression()
        {
            return this.IdentifierExpression;
        }

        protected override void EmitConversionExpression()
        {
            this.VisitIdentifierExpression();
        }

        protected IMemberDefinition ResolveFieldOrMethod(string name, int genericCount)
        {
            bool allowPrivate = true;
            TypeDefinition type = this.Emitter.GetTypeDefinition();
            TypeDefinition thisType = type;

            while (true)
            {
                foreach (MethodDefinition method in type.Methods)
                {
                    if (method.Name != name || method.GenericParameters.Count != genericCount)
                    {
                        continue;
                    }

                    if (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly)
                    {
                        return method;
                    }

                    if (method.IsPrivate && allowPrivate)
                    {
                        return method;
                    }

                    if (method.IsAssembly && type.Module.Mvid == thisType.Module.Mvid)
                    {
                        return method;
                    }
                }

                foreach (FieldDefinition field in type.Fields)
                {
                    if (field.Name != name)
                    {
                        continue;
                    }

                    if (field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly)
                    {
                        return field;
                    }

                    if (field.IsPrivate && allowPrivate)
                    {
                        return field;
                    }

                    if (field.IsAssembly && type.Module.Mvid == thisType.Module.Mvid)
                    {
                        return field;
                    }
                }

                type = this.Emitter.GetBaseTypeDefinition(type);

                if (type == null)
                {
                    break;
                }

                allowPrivate = false;
            }

            return null;
        }

        protected void VisitIdentifierExpression()
        {
            IdentifierExpression identifierExpression = this.IdentifierExpression;

            ResolveResult resolveResult = null;
            ResolveResult expressionResolveResult = null;

            if (identifierExpression.Parent is InvocationExpression && (((InvocationExpression)(identifierExpression.Parent)).Target == identifierExpression))
            {
                resolveResult = this.Emitter.Resolver.ResolveNode(identifierExpression.Parent, this.Emitter);
                expressionResolveResult = this.Emitter.Resolver.ResolveNode(identifierExpression, this.Emitter);
            }
            else
            {
                resolveResult = this.Emitter.Resolver.ResolveNode(identifierExpression, this.Emitter);
            }

            var id = identifierExpression.Identifier;
            //this.Emitter.Validator.CheckIdentifier(id, identifierExpression);

            var isResolved = resolveResult != null && !(resolveResult is ErrorResolveResult);
            var memberResult = resolveResult as MemberResolveResult;

            if (this.Emitter.Locals != null && this.Emitter.Locals.ContainsKey(id))
            {
                if (this.Emitter.LocalsMap != null && this.Emitter.LocalsMap.ContainsKey(id) && !(identifierExpression.Parent is DirectionExpression))
                {
                    this.Write(this.Emitter.LocalsMap[id]);
                }
                else if (this.Emitter.LocalsNamesMap != null && this.Emitter.LocalsNamesMap.ContainsKey(id))
                {
                    this.Write(this.Emitter.LocalsNamesMap[id]);
                }
                else
                {
                    this.Write(id);
                }

                Helpers.CheckValueTypeClone(resolveResult, identifierExpression, this);

                return;
            }

            IMemberDefinition member = this.ResolveFieldOrMethod(id, identifierExpression.TypeArguments.Count);

            if (member == null && memberResult != null)
            {
                var iMethod = memberResult.Member as IMethod;

                if (iMethod != null)
                {
                    member = this.ResolveFieldOrMethod(id, iMethod.TypeParameters.Count);
                }
            }

            if (member != null)
            {
                MethodDefinition method = member as MethodDefinition;
                FieldDefinition field = member as FieldDefinition;
                bool isStatic = method != null && method.IsStatic || field != null && field.IsStatic;
                string appendAdditionalCode = null;

                if (method != null)
                {
                    string inline = this.Emitter.GetInline(method);

                    if (!string.IsNullOrWhiteSpace(inline))
                    {
                        return;
                    }

                    if (memberResult != null &&
                         memberResult.Member is IMethod &&
                         !(
                            identifierExpression.Parent is InvocationExpression &&
                            identifierExpression.NextSibling != null &&
                            identifierExpression.NextSibling.Role is TokenRole &&
                            ((TokenRole)identifierExpression.NextSibling.Role).Token == "("
                         )
                    )
                    {
                        var resolvedMethod = (IMethod)memberResult.Member;                        

                        if (!isStatic)
                        {
                            var isExtensionMethod = resolvedMethod.IsExtensionMethod;
                            this.Write(Bridge.Translator.Emitter.ROOT + "." + (isExtensionMethod ? Bridge.Translator.Emitter.DELEGATE_BIND_SCOPE : Bridge.Translator.Emitter.DELEGATE_BIND) + "(");
                            this.WriteThis();
                            this.Write(", ");
                            appendAdditionalCode = ")";
                        }
                    }
                }

                if (isStatic)
                {
                    this.Write(this.Emitter.ShortenTypeName(Helpers.GetScriptFullName(member.DeclaringType)));
                }
                else
                {
                    this.WriteThis();
                }

                this.WriteDot();

                if (method != null)
                {
                    if (resolveResult is InvocationResolveResult)
                    {
                        InvocationResolveResult invocationResult = (InvocationResolveResult)resolveResult;
                        CSharpInvocationResolveResult cInvocationResult = (CSharpInvocationResolveResult)resolveResult;
                        var expresssionMember = expressionResolveResult as MemberResolveResult;

                        if (expresssionMember != null &&
                            cInvocationResult != null &&
                            cInvocationResult.IsDelegateInvocation &&
                            invocationResult.Member != expresssionMember.Member)
                        {
                            this.Write(OverloadsCollection.Create(this.Emitter, expresssionMember.Member).GetOverloadName());
                        }
                        else
                        {
                            this.Write(OverloadsCollection.Create(this.Emitter, invocationResult.Member).GetOverloadName());
                        }    
                    }
                    else if (resolveResult is MemberResolveResult)
                    {
                        MemberResolveResult memberResolveResult = (MemberResolveResult)resolveResult;
                        this.Write(OverloadsCollection.Create(this.Emitter, memberResolveResult.Member).GetOverloadName());
                    }
                    else
                    {
                        this.Write(this.Emitter.GetDefinitionName(method));
                    }

                    if (appendAdditionalCode != null)
                    {
                        this.Write(appendAdditionalCode);
                    }
                }
                else
                {
                    bool isConst = this.Emitter.IsMemberConst(memberResult.Member);

                    if (isConst && this.Emitter.IsInlineConst(memberResult.Member))
                    {
                        this.WriteScript(memberResult.ConstantValue);
                    }
                    else if (memberResult != null && memberResult.Member is DefaultResolvedEvent && this.Emitter.IsAssignment && (this.Emitter.AssignmentType == AssignmentOperatorType.Add || this.Emitter.AssignmentType == AssignmentOperatorType.Subtract))
                    {
                        this.Write(this.Emitter.AssignmentType == AssignmentOperatorType.Add ? "add" : "remove");
                        this.Write(OverloadsCollection.Create(this.Emitter, memberResult.Member).GetOverloadName());
                        this.WriteOpenParentheses();
                    }
                    else
                    {
                        if (resolveResult is InvocationResolveResult)
                        {
                            InvocationResolveResult invocationResult = (InvocationResolveResult)resolveResult;
                            CSharpInvocationResolveResult cInvocationResult = (CSharpInvocationResolveResult)resolveResult;
                            var expresssionMember = expressionResolveResult as MemberResolveResult;

                            if (expresssionMember != null &&
                                cInvocationResult != null &&
                                cInvocationResult.IsDelegateInvocation &&
                                invocationResult.Member != expresssionMember.Member)
                            {
                                this.Write(OverloadsCollection.Create(this.Emitter, expresssionMember.Member).GetOverloadName());
                            }
                            else
                            {
                                this.Write(OverloadsCollection.Create(this.Emitter, invocationResult.Member).GetOverloadName());
                            }
                        }
                        else
                        {
                            this.Write(OverloadsCollection.Create(this.Emitter, memberResult.Member).GetOverloadName());
                        }
                    }
                }

                Helpers.CheckValueTypeClone(resolveResult, identifierExpression, this);

                return;
            }

            string resolved = this.Emitter.ResolveNamespaceOrType(id, true);

            if (!String.IsNullOrEmpty(resolved))
            {
                if (this.Emitter.TypeDefinitions.ContainsKey(resolved))
                {
                    resolved = this.Emitter.ShortenTypeName(resolved);
                }

                this.Write(resolved);

                return;
            }

            string inlineCode = memberResult != null ? this.Emitter.GetInline(memberResult.Member) : null;
            bool hasInline = !string.IsNullOrEmpty(inlineCode);
            bool hasThis = hasInline && inlineCode.Contains("{this}");
            
            if (hasThis)
            {
                this.Write("");
                var oldBuilder = this.Emitter.Output;
                this.Emitter.Output = new StringBuilder();

                if (memberResult.Member.IsStatic)
                {
                    this.Write(this.Emitter.ShortenTypeName(Helpers.GetScriptFullName(memberResult.Member.DeclaringType)));
                }
                else
                {
                    this.WriteThis();
                }

                inlineCode = inlineCode.Replace("{this}", this.Emitter.Output.ToString());
                this.Emitter.Output = oldBuilder;

                if (resolveResult is InvocationResolveResult)
                {
                    this.PushWriter(inlineCode);
                }
                else
                {
                    this.Write(inlineCode);
                }

                return;
            }

            if (hasInline && memberResult.Member.IsStatic)
            {
                if (resolveResult is InvocationResolveResult)
                {
                    this.PushWriter(inlineCode);
                }
                else
                {
                    this.Write(inlineCode);
                }
            }            
            else if (memberResult != null && memberResult.Member.SymbolKind == SymbolKind.Property && memberResult.TargetResult.Type.Kind != TypeKind.Anonymous)
            {
                bool isStatement = false;
                string valueVar = null;

                if (this.Emitter.IsUnaryAccessor)
                {
                    isStatement = identifierExpression.Parent is UnaryOperatorExpression && identifierExpression.Parent.Parent is ExpressionStatement;

                    if (NullableType.IsNullable(memberResult.Type))
                    {
                        isStatement = false;
                    }

                    if (!isStatement)
                    {
                        this.WriteOpenParentheses();

                        valueVar = this.GetTempVarName();

                        this.Write(valueVar);
                        this.Write(" = ");
                    }                    
                }
                
                this.WriteTarget(memberResult);

                if (!string.IsNullOrWhiteSpace(inlineCode))
                {
                    this.Write(inlineCode);
                }
                else if (Helpers.IsFieldProperty(memberResult.Member, this.Emitter))
                {
                    this.Write(Helpers.GetPropertyRef(memberResult.Member, this.Emitter));
                }
                else if (!this.Emitter.IsAssignment)
                {
                    if (this.Emitter.IsUnaryAccessor)
                    {
                        if (isStatement)
                        {
                            this.Write(Helpers.GetPropertyRef(memberResult.Member, this.Emitter, true));
                            this.WriteOpenParentheses();

                            this.WriteTarget(memberResult);

                            this.Write(Helpers.GetPropertyRef(memberResult.Member, this.Emitter, false));
                            this.WriteOpenParentheses();
                            this.WriteCloseParentheses();

                            if (this.Emitter.UnaryOperatorType == UnaryOperatorType.Increment || this.Emitter.UnaryOperatorType == UnaryOperatorType.PostIncrement)
                            {
                                this.Write("+");
                            }
                            else
                            {
                                this.Write("-");
                            }

                            this.Write("1");
                            this.WriteCloseParentheses();
                        }
                        else
                        {
                            this.Write(Helpers.GetPropertyRef(memberResult.Member, this.Emitter, false));
                            this.WriteOpenParentheses();
                            this.WriteCloseParentheses();
                            this.WriteComma();

                            this.WriteTarget(memberResult);
                            this.Write(Helpers.GetPropertyRef(memberResult.Member, this.Emitter, true));
                            this.WriteOpenParentheses();
                            this.Write(valueVar);

                            if (this.Emitter.UnaryOperatorType == UnaryOperatorType.Increment || this.Emitter.UnaryOperatorType == UnaryOperatorType.PostIncrement)
                            {
                                this.Write("+");
                            }
                            else
                            {
                                this.Write("-");
                            }

                            this.Write("1");
                            this.WriteCloseParentheses();
                            this.WriteComma();

                            this.Write(valueVar);

                            if (this.Emitter.UnaryOperatorType == UnaryOperatorType.Increment || this.Emitter.UnaryOperatorType == UnaryOperatorType.Decrement)
                            {
                                if (this.Emitter.UnaryOperatorType == UnaryOperatorType.Increment)
                                {
                                    this.Write("+");
                                }
                                else
                                {
                                    this.Write("-");
                                }

                                this.Write("1");
                            }

                            this.WriteCloseParentheses();

                            if (valueVar != null)
                            {
                                this.RemoveTempVar(valueVar);
                            }
                        }
                    }
                    else
                    {
                        this.Write(Helpers.GetPropertyRef(memberResult.Member, this.Emitter));
                        this.WriteOpenParentheses();
                        this.WriteCloseParentheses();
                    }
                }
                else if (this.Emitter.AssignmentType != AssignmentOperatorType.Assign)
                {
                    string trg;

                    if (memberResult.Member.IsStatic)
                    {
                        trg = this.Emitter.ShortenTypeName(Helpers.GetScriptFullName(memberResult.Member.DeclaringType));
                    }
                    else
                    {
                        trg = "this";
                    }

                    this.PushWriter(string.Concat(Helpers.GetPropertyRef(memberResult.Member, this.Emitter, true),
                        "(",
                        trg,
                        ".",
                        Helpers.GetPropertyRef(memberResult.Member, this.Emitter, false),
                        "()",
                        "{0})"));
                }
                else
                {
                    this.PushWriter(Helpers.GetPropertyRef(memberResult.Member, this.Emitter, true) + "({0})");
                }
            }
            else if (memberResult != null && memberResult.Member is DefaultResolvedEvent && this.Emitter.IsAssignment && (this.Emitter.AssignmentType == AssignmentOperatorType.Add || this.Emitter.AssignmentType == AssignmentOperatorType.Subtract))
            {
                this.WriteTarget(memberResult);

                if (!string.IsNullOrWhiteSpace(inlineCode))
                {
                    this.Write(inlineCode);
                }
                else
                {
                    this.Write(this.Emitter.AssignmentType == AssignmentOperatorType.Add ? "add" : "remove");
                    this.Write(OverloadsCollection.Create(this.Emitter, memberResult.Member, this.Emitter.AssignmentType == AssignmentOperatorType.Subtract).GetOverloadName());
                }

                this.WriteOpenParentheses();
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(inlineCode))
                {
                    this.Write(inlineCode);
                }
                else if (isResolved)
                {
                    if (resolveResult is TypeResolveResult)
                    {
                        var typeResolveResult = (TypeResolveResult)resolveResult;

                        this.Write(this.Emitter.ShortenTypeName(Helpers.GetScriptFullName(typeResolveResult.Type)));

                        if (typeResolveResult.Type.TypeParameterCount > 0)
                        {
                            this.WriteOpenParentheses();
                            new TypeExpressionListBlock(this.Emitter, this.IdentifierExpression.TypeArguments).Emit();
                            this.WriteCloseParentheses();
                        }
                    }
                    else if (resolveResult is LocalResolveResult)
                    {
                        var localResolveResult = (LocalResolveResult)resolveResult;
                        this.Write(localResolveResult.Variable.Name);
                    }
                    else if (memberResult != null)
                    {
                        this.WriteTarget(memberResult);
                        this.Write(this.Emitter.GetEntityName(memberResult.Member));
                    }
                    else
                    {
                        this.Write(resolveResult.ToString());
                    }

                    Helpers.CheckValueTypeClone(resolveResult, identifierExpression, this);
                }
                else
                {
                    throw (Exception)this.Emitter.CreateException(identifierExpression, "Cannot resolve identifier: " + id);
                }
            }
        }

        protected void WriteTarget(MemberResolveResult memberResult)
        {
            if (memberResult.Member.IsStatic)
            {
                this.Write(this.Emitter.ShortenTypeName(Helpers.GetScriptFullName(memberResult.Member.DeclaringType)));
            }
            else
            {
                this.WriteThis();
            }

            this.WriteDot();
        }
    }
}