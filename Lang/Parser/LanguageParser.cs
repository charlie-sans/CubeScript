﻿using System;
using System.Collections.Generic;
using System.Linq;
using Lang.AST;
using Lang.Data;
using Lang.Exceptions;
using Lang.Utils;

namespace Lang.Parser
{
    public class LanguageParser
    {
        private Random _rand = new Random();

        private ParseableTokenStream TokenStream { get; set; }

        public LanguageParser(Tokenizer tokenizer)
        {
            TokenStream = new ParseableTokenStream(tokenizer);
        }

        public Ast Parse()
        {
            var statements = GetStatements(() => TokenStream.Current.TokenType ==  TokenType.EOF);

            return new ScopeDeclr(statements);
        }

        #region Entry Point

        /// <summary>
        /// List of statements in the main program body
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="end"></param>
        private List<Ast> GetStatements(Func<Boolean> end)
        {
            var aggregate = new List<Ast>(1024);

            while (!end())
            {
                if (TokenStream.Current.TokenType == TokenType.LBracket)
                {
                    var statements = GetExpressionsInScope(TokenType.LBracket, TokenType.RBracket);

                    aggregate.Add(statements);
                }
                else
                {
                    var statement = Statement();

                    aggregate.Add(statement);
                }
            }

            return aggregate;
        }

        /// <summary>
        /// Method declaration or regular statement
        /// </summary>
        /// <returns></returns>
        private Ast Statement()
        {
            if (TokenStream.Alt(MethodDeclaration))
            {
                return TokenStream.Get(MethodDeclaration);
            }

            var statement = Expression();

            if (TokenStream.Current.TokenType == TokenType.SemiColon)
            {
                TokenStream.Take(TokenType.SemiColon);
            }

            return statement;
        }

        #endregion

        #region Statement Parsers

        #region Single statement 

        /// <summary>
        /// A statement inside of a valid scope 
        /// </summary>
        /// <returns></returns>
        private Ast Expression()
        {
            // ordering here matters since it resolves to precedence
            var ast = ScopeStart().Or(LambdaStatement)
                                  .Or(VariableDeclWithAssignStatement)
                                  .Or(VariableDeclrStatement)
                                  .Or(GetIf)
                                  .Or(GetWhile)
                                  .Or(OperationExpression);

            if (ast != null)
            {
                return ast;
            }

            throw new InvalidSyntax(String.Format("Unknown expression type {0} - {1}", TokenStream.Current.TokenType, TokenStream.Current.TokenValue));
        }

        private Ast ScopeStart()
        {
            if (TokenStream.Current.TokenType == TokenType.LBracket)
            {
                var statements = GetExpressionsInScope(TokenType.LBracket, TokenType.RBracket);

                return statements;
            }

            return null;
        }

        #endregion

        #region Expressions of single items or expr op expr

        private Ast OperationExpression()
        {
            switch (TokenStream.Current.TokenType)
            {
                case TokenType.QuotedString:
                case TokenType.Word:
                    return ParseOperationExpression();

                case TokenType.OpenParenth:
                    return GetExpressionsInScope(TokenType.OpenParenth, TokenType.CloseParenth, false);

                default:
                    return null;
            }
        }

        private Ast ParseOperationExpression()
        {
            Func<Ast> op = () =>
                {
                    var left = FunctionCallStatement().Or(SingleToken);

                    return new Expr(left, Operator(), Expression());
                };

            if(TokenStream.Alt(op))
            {
                return TokenStream.Get(op);
            }
            
            if (TokenStream.Alt(ConsumeFinalExpression))
            {
                return TokenStream.Get(ConsumeFinalExpression);
            }

            return null;
        }

        #endregion

        #region Conditionals and Loops

        private Ast GetWhile()
        {
            if (TokenStream.Current.TokenType == TokenType.While && TokenStream.Alt(ParseWhile))
            {
                return TokenStream.Get(ParseWhile);
            }

            return null;
        }

        private Ast GetIf()
        {
            if (TokenStream.Current.TokenType == TokenType.If && TokenStream.Alt(ParseIf))
            {
                return TokenStream.Get(ParseIf);
            }

            return null;
        }

        private WhileLoop ParseWhile()
        {
            var predicateAndExpressions = GetPredicateAndExpressions(TokenType.While);

            var predicate = predicateAndExpressions.Item1;
            var statements = predicateAndExpressions.Item2;

            return new WhileLoop(predicate, statements);
        }


        private Conditional ParseIf()
        {
            var predicateAndExpressions = GetPredicateAndExpressions(TokenType.If);

            var predicate = predicateAndExpressions.Item1;
            var statements = predicateAndExpressions.Item2;

            // no else following, then just basic if statement
            if (TokenStream.Current.TokenType != TokenType.Else)
            {
                return new Conditional(new Token(TokenType.If), predicate, statements);
            }

            // we found an else if scenario
            if (TokenStream.Peek(1).TokenType == TokenType.If)
            {
                TokenStream.Take(TokenType.Else);

                var alternate = ParseIf();

                return new Conditional(new Token(TokenType.If), predicate, statements, alternate);
            }

            // found a trailing else
            return new Conditional(new Token(TokenType.If), predicate, statements, ParseTrailingElse());
        }

        private Conditional ParseTrailingElse()
        {
            TokenStream.Take(TokenType.Else);

            var statements = GetExpressionsInScope(TokenType.LBracket, TokenType.RBracket);

            return new Conditional(new Token(TokenType.Else), null, statements);
        }

        #endregion

        #region Function parsing (lambdas, declarations, arguments)

        private Ast FunctionCall()
        {
            var name = TokenStream.Take(TokenType.Word);

            var args = GetArgumentList();

            return new FuncInvoke(name, args);
        }


        private Ast Lambda()
        {
            TokenStream.Take(TokenType.Fun);

            var arguments = GetArgumentList(true);

            TokenStream.Take(TokenType.DeRef);

            var lines = GetExpressionsInScope(TokenType.LBracket, TokenType.RBracket);

            var method = new MethodDeclr(new Token(TokenType.Fun), 
                                         new Token(TokenType.Void),
                                         new Token(TokenType.Word, AnonymousFunctionName), 
                                         arguments, 
                                         lines);


            return method;
        }

        private string AnonymousFunctionName
        {
            get { return "anonymous" + _rand.Next(); }
        }

        private Ast MethodDeclaration()
        {
            if (!IsValidMethodReturnType())
            {
                throw new InvalidSyntax("Invalid syntax");
            }

            // return type
            var returnType = TokenStream.Take(TokenStream.Current.TokenType);

            // func name
            var funcName = TokenStream.Take(TokenType.Word);

            var argList = GetArgumentList(true);

            var innerExpressions = GetExpressionsInScope(TokenType.LBracket, TokenType.RBracket);

            return new MethodDeclr(new Token(TokenType.ScopeStart), returnType, funcName, argList, innerExpressions);
        }

        private List<Ast> GetArgumentList(bool includeType = false)
        {
            TokenStream.Take(TokenType.OpenParenth);

            var args = new List<Ast>(64);

            while (TokenStream.Current.TokenType != TokenType.CloseParenth)
            {
                var argument = includeType ? VariableDeclaration() : Expression();

                args.Add(argument);

                if (TokenStream.Current.TokenType == TokenType.Comma)
                {
                    TokenStream.Take(TokenType.Comma);
                }
            }

            TokenStream.Take(TokenType.CloseParenth);

            return args;
        }

        #endregion

        #region Variable Declrations and Assignments

        private Ast VariableDeclarationAndAssignment()
        {
            if (IsValidMethodReturnType() && IsValidVariableName(TokenStream.Peek(1)))
            {
                var type = TokenStream.Take(TokenStream.Current.TokenType);

                var name = TokenStream.Take(TokenType.Word);

                TokenStream.Take(TokenType.Equals);

                return new VarDeclrAst(type, name, Expression());
            }

            return null;
        }

        private Ast VariableDeclaration()
        {
            if (IsValidMethodReturnType() && IsValidVariableName(TokenStream.Peek(1)))
            {
                var type = TokenStream.Take(TokenStream.Current.TokenType);

                var name = TokenStream.Take(TokenType.Word);

                // variable declrations are independent and have no following expressions
                // but the semicolon will be consumed elsewhere
                if (TokenStream.Current.TokenType != TokenType.SemiColon)
                {
                    return null;
                }

                return new VarDeclrAst(type, name);
            }

            return null;
        }

        private Ast VariableAssignment()
        {
            var name = TokenStream.Take(TokenType.Word);

            var equals = TokenStream.Take(TokenType.Equals);

            return new Expr(new Expr(name), equals, Expression());
        }

        #endregion

        #region Single Expressions or Tokens

        private Ast ConsumeFinalExpression()
        {
            return FunctionCallStatement().Or(VariableAssignmentStatement)
                                          .Or(SingleToken);
        }

        private Ast SingleToken()
        {
            var token = new Expr(TokenStream.Take(TokenStream.Current.TokenType));

            return token;
        }

        private Token Operator()
        {
            if (IsOperator(TokenStream.Current))
            {
                return TokenStream.Take(TokenStream.Current.TokenType);
            }

            throw new InvalidSyntax(String.Format("Invalid token found. Expected operator but found {0} - {1}", TokenStream.Current.TokenType, TokenStream.Current.TokenValue));
        }

        #endregion

        #region Helpers

        private Tuple<Ast, ScopeDeclr> GetPredicateAndExpressions(TokenType type)
        {
            TokenStream.Take(type
                );
            TokenStream.Take(TokenType.OpenParenth);

            var predicate = Expression();

            TokenStream.Take(TokenType.CloseParenth);

            var statements = GetExpressionsInScope(TokenType.LBracket, TokenType.RBracket);

            return new Tuple<Ast, ScopeDeclr>(predicate, statements);
        } 

        private ScopeDeclr GetExpressionsInScope(TokenType open, TokenType close, bool expectSemicolon = true)
        {
            TokenStream.Take(open);
            var lines = new List<Ast>();
            while (TokenStream.Current.TokenType != close)
            {
                var statement = Expression();

                lines.Add(statement);

                if (expectSemicolon && StatementExpectsSemiColon(statement))
                {
                    TokenStream.Take(TokenType.SemiColon);
                }
            }

            TokenStream.Take(close);

            return new ScopeDeclr(lines);
        }

        private bool StatementExpectsSemiColon(Ast statement)
        {
            return !(statement is MethodDeclr || statement is Conditional || statement is WhileLoop);
        }

        #endregion

        #endregion

        #region TokenStream.Alternative Route Testers

        private Ast VariableDeclWithAssignStatement()
        {
            if (TokenStream.Alt(VariableDeclarationAndAssignment))
            {
                return TokenStream.Get(VariableDeclarationAndAssignment);
            }

            return null;
        }

        private Ast VariableAssignmentStatement()
        {
            if (TokenStream.Alt(VariableAssignment))
            {
                return TokenStream.Get(VariableAssignment);
            }

            return null;
        }

        private Ast VariableDeclrStatement()
        {
            if (TokenStream.Alt(VariableDeclaration))
            {
                var declr = TokenStream.Get(VariableDeclaration);

                return declr;
            }

            return null;
        }

        private Ast FunctionCallStatement()
        {
            if (TokenStream.Alt(FunctionCall))
            {
                return TokenStream.Get(FunctionCall);
            }

            return null;
        }

        private Ast LambdaStatement()
        {
            if (TokenStream.Alt(Lambda))
            {
                return TokenStream.Get(Lambda);
            }

            return null;
        }

        private bool IsValidMethodReturnType()
        {
            switch (TokenStream.Current.TokenType)
            {
                case TokenType.Void:
                case TokenType.Word:
                case TokenType.Int:
                    return true;
            }
            return false;
        }

        private bool IsValidVariableName(Token item)
        {
            switch (item.TokenType)
            {
                case TokenType.Word:
                    return true;
            }
            return false;
        }

        private Boolean IsOperator(Token item)
        {
            switch (item.TokenType)
            {
                case TokenType.Equals:
                case TokenType.Plus:
                case TokenType.Minus:
                case TokenType.Asterix:
                case TokenType.Carat:
                case TokenType.Slash:
                    return true;
            }
            return false;
        }

        #endregion
    }
}
