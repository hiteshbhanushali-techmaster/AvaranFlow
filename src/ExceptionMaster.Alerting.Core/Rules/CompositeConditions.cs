using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ExceptionMaster.Alerting.Rules
{
    /// <summary>
    /// Composite condition that requires ALL child conditions to evaluate to true.
    /// Internal AST node; constructed via RuleCondition.And(...).
    /// </summary>
    internal sealed class AndCondition : RuleCondition
    {
        public IReadOnlyList<RuleCondition> Children { get; }

        public AndCondition(IEnumerable<RuleCondition> children)
        {
            if (children == null) throw new ArgumentNullException(nameof(children));
            Children = new ReadOnlyCollection<RuleCondition>(children.Where(c => c != null).ToList());
        }

        internal override bool Evaluate(RuleEvaluationContext context)
        {
            if (context == null) return false;
            if (Children.Count == 0) return true;

            for (int i = 0; i < Children.Count; i++)
            {
                if (!Children[i].Evaluate(context))
                {
                    return false; // Short-circuit
                }
            }

            return true;
        }

        internal override void Validate(List<string> errors)
        {
            if (Children.Count == 0)
            {
                errors.Add("AndCondition must contain at least one child condition.");
            }

            for (int i = 0; i < Children.Count; i++)
            {
                Children[i].Validate(errors);
            }
        }

        public override string ToString()
        {
            return $"({string.Join(" AND ", Children.Select(c => c.ToString()))})";
        }
    }

    /// <summary>
    /// Composite condition that evaluates to true if ANY child condition evaluates to true.
    /// Internal AST node; constructed via RuleCondition.Or(...).
    /// </summary>
    internal sealed class OrCondition : RuleCondition
    {
        public IReadOnlyList<RuleCondition> Children { get; }

        public OrCondition(IEnumerable<RuleCondition> children)
        {
            if (children == null) throw new ArgumentNullException(nameof(children));
            Children = new ReadOnlyCollection<RuleCondition>(children.Where(c => c != null).ToList());
        }

        internal override bool Evaluate(RuleEvaluationContext context)
        {
            if (context == null) return false;
            if (Children.Count == 0) return false;

            for (int i = 0; i < Children.Count; i++)
            {
                if (Children[i].Evaluate(context))
                {
                    return true; // Short-circuit
                }
            }

            return false;
        }

        internal override void Validate(List<string> errors)
        {
            if (Children.Count == 0)
            {
                errors.Add("OrCondition must contain at least one child condition.");
            }

            for (int i = 0; i < Children.Count; i++)
            {
                Children[i].Validate(errors);
            }
        }

        public override string ToString()
        {
            return $"({string.Join(" OR ", Children.Select(c => c.ToString()))})";
        }
    }

    /// <summary>
    /// Unary condition that inverts the result of an inner condition.
    /// Internal AST node; constructed via RuleCondition.Not(...).
    /// </summary>
    internal sealed class NotCondition : RuleCondition
    {
        public RuleCondition Inner { get; }

        public NotCondition(RuleCondition inner)
        {
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        internal override bool Evaluate(RuleEvaluationContext context)
        {
            if (context == null) return false;
            return !Inner.Evaluate(context);
        }

        internal override void Validate(List<string> errors)
        {
            if (Inner == null)
            {
                errors.Add("NotCondition must contain a non-null Inner condition.");
            }
            else
            {
                Inner.Validate(errors);
            }
        }

        public override string ToString()
        {
            return $"NOT ({Inner})";
        }
    }
}
