﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lang.Data;

namespace Lang.AST
{
    public class VarDeclrAst : Ast
    {
        public Ast DeclarationType { get; private set; }

        public Ast VariableValue { get; private set; }

        public Ast VariableName { get; private set; }

        protected VarDeclrAst(Token token) : base(token)
        {
        }

        public VarDeclrAst(Token declType, Token name)
            : base(name)
        {
            DeclarationType = new Expr(declType);
        }

        public VarDeclrAst(Token declType, Token name, Ast value)
            : base(name)
        {
            DeclarationType = new Expr(declType);

            VariableValue = value;

            VariableName = new Expr(name);
        }

    }
}
