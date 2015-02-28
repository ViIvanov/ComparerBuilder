using System;
using System.Linq.Expressions;

namespace GBricks.Collections
{
  internal sealed class ReplaceVisitor : ExpressionVisitor
  {
    private ReplaceVisitor(Expression what, Expression to) {
      if(what == null) {
        throw new ArgumentNullException(nameof(what));
      } else if(to == null) {
        throw new ArgumentNullException(nameof(to));
      }//if
  
      What = what;
      To = to;
    }

    private ReplaceVisitor(Expression what, Expression to, Expression secondWhat, Expression secondTo) : this(what, to) {
      if(secondWhat == null) {
        throw new ArgumentNullException(nameof(secondWhat));
      } else if(secondTo == null) {
        throw new ArgumentNullException(nameof(secondTo));
      }//if
  
      SecondWhat = secondWhat;
      SecondTo = secondTo;
    }
  
    public Expression What { get; }
    public Expression To { get; }
  
    public Expression SecondWhat { get; }
    public Expression SecondTo { get; }

    public static Expression ReplaceParameters(LambdaExpression expression, Expression first, Expression second = null) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      } else if(expression.Parameters.Count != (second == null ? 1 : 2)) {
        throw new ArgumentException($"{nameof(expression)}.Parameters.Count != {(second == null ? 1 : 2)}", nameof(expression));
      }//if

      var replace = second == null
        ? new ReplaceVisitor(expression.Parameters[0], first)
        : new ReplaceVisitor(expression.Parameters[0], first, expression.Parameters[1], second);
      return replace.Visit(expression.Body);
    }

    public override Expression Visit(Expression node) {
      if(node == What) {
        return To;
      } else if(node != null && node == SecondWhat) {
        return SecondTo;
      } else {
        return base.Visit(node);
      }//if
    }
  }
}
