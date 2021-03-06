﻿using Bridge.Contract;
using Mono.Cecil;
using System.Collections.Generic;

namespace Bridge.Translator
{
    public partial class Emitter : Visitor, IEmitter
    {
        public Emitter(IDictionary<string, TypeDefinition> typeDefinitions, List<ITypeInfo> types, IValidator validator)
        {
            this.TypeDefinitions = typeDefinitions;
            this.Types = types;
            this.Types.Sort(this.CompareTypeInfos);
            this.Validator = validator;
            this.AssignmentType = ICSharpCode.NRefactory.CSharp.AssignmentOperatorType.Any;
            this.UnaryOperatorType = ICSharpCode.NRefactory.CSharp.UnaryOperatorType.Any;
        }

        public virtual Dictionary<string, string> Emit()
        {
            new EmitBlock(this).Emit();
            return this.TransformOutputs();
        }        
    }
}
