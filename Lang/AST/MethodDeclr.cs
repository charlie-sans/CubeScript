﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lang.Data;
using Lang.Visitors;

namespace Lang.AST
{
    public class MethodDeclr : Ast
    {
        public Ast MethodName { get; private set; }

        public Ast MethodReturnType { get; private set; }

        public List<Ast> Arguments { get; private set; }

        public ScopeDeclr BodyStatements { get; private set; }

        public MethodDeclr(Token token, Token returnType, Token funcName, List<Ast> arguments, ScopeDeclr body)
            : base(token)
        {
            MethodReturnType = new Expr(returnType);

            MethodName = new Expr(funcName);

            Arguments = arguments;

            BodyStatements = body;
        }


        public override void Visit(IAstVisitor visitor)
        {
            visitor.Visit(this);
        }
    }
}
