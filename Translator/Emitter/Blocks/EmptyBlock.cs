﻿using Bridge.Contract;
using ICSharpCode.NRefactory.CSharp;

namespace Bridge.Translator
{
    public class EmptyBlock : AbstractEmitterBlock
    {
        public EmptyBlock(IEmitter emitter, EmptyStatement emptyStatement)
        {
            this.Emitter = emitter;
            this.EmptyStatement = emptyStatement;
        }

        public EmptyStatement EmptyStatement 
        { 
            get; 
            set; 
        }

        public override void Emit()
        {
            this.WriteSemiColon(true);   
        }
    }
}