using Esprima.Ast;
using Jint.Native;
using Jint.Native.Function;
using Jint.Runtime.Environments;
using Jint.Runtime.References;

namespace Jint.Runtime.Interpreter.Expressions
{
    internal sealed class JintAssignmentExpression : JintExpression
    {
        private readonly JintExpression _left;
        private readonly JintExpression _right;
        private readonly AssignmentOperator _operator;

        private JintAssignmentExpression(Engine engine, AssignmentExpression expression) : base(engine, expression)
        {
            _left = Build(engine, expression.Left);
            _right = Build(engine, expression.Right);
            _operator = expression.Operator;
        }

        internal static JintExpression Build(Engine engine, AssignmentExpression expression)
        {
            if (expression.Operator == AssignmentOperator.Assign)
            {
                if (expression.Left is BindingPattern)
                {
                    return new BindingPatternAssignmentExpression(engine, expression);
                }

                return new SimpleAssignmentExpression(engine, expression);
            }

            return new JintAssignmentExpression(engine, expression);
        }

        protected override object EvaluateInternal()
        {
            var lref = _left.Evaluate() as Reference;
            if (lref is null)
            {
                ExceptionHelper.ThrowReferenceError(_engine.Realm, "not a valid reference");
            }

            var rval = _right.GetValue();
            var lval = _engine.GetValue(lref, false);
            var handledByOverload = false;

            if (_engine.Options._IsOperatorOverloadingAllowed)
            {
                string operatorClrName = null;
                switch (_operator)
                {
                    case AssignmentOperator.PlusAssign:
                        operatorClrName = "op_Addition";
                        break;
                    case AssignmentOperator.MinusAssign:
                        operatorClrName = "op_Subtraction";
                        break;
                    case AssignmentOperator.TimesAssign:
                        operatorClrName = "op_Multiply";
                        break;
                    case AssignmentOperator.DivideAssign:
                        operatorClrName = "op_Division";
                        break;
                    case AssignmentOperator.ModuloAssign:
                        operatorClrName = "op_Modulus";
                        break;
                    case AssignmentOperator.BitwiseAndAssign:
                        operatorClrName = "op_BitwiseAnd";
                        break;
                    case AssignmentOperator.BitwiseOrAssign:
                        operatorClrName = "op_BitwiseOr";
                        break;
                    case AssignmentOperator.BitwiseXOrAssign:
                        operatorClrName = "op_ExclusiveOr";
                        break;
                    case AssignmentOperator.LeftShiftAssign:
                        operatorClrName = "op_LeftShift";
                        break;
                    case AssignmentOperator.RightShiftAssign:
                        operatorClrName = "op_RightShift";
                        break;
                    case AssignmentOperator.UnsignedRightShiftAssign:
                        operatorClrName = "op_UnsignedRightShift";
                        break;
                    case AssignmentOperator.ExponentiationAssign:
                    case AssignmentOperator.Assign:
                    default:
                        break;
                }

                if (operatorClrName != null &&
                    JintBinaryExpression.TryOperatorOverloading(_engine, lval, rval, operatorClrName, out var result))
                {
                    lval = JsValue.FromObject(_engine, result);
                    handledByOverload = true;
                }
            }

            if (!handledByOverload)
            {
                switch (_operator)
                {
                    case AssignmentOperator.PlusAssign:
                        if (AreIntegerOperands(lval, rval))
                        {
                            lval = (long) lval.AsInteger() + rval.AsInteger();
                        }
                        else
                        {
                            var lprim = TypeConverter.ToPrimitive(lval);
                            var rprim = TypeConverter.ToPrimitive(rval);

                            if (lprim.IsString() || rprim.IsString())
                            {
                                if (!(lprim is JsString jsString))
                                {
                                    jsString = new JsString.ConcatenatedString(TypeConverter.ToString(lprim));
                                }

                                lval = jsString.Append(rprim);
                            }
                            else
                            {
                                lval = TypeConverter.ToNumber(lprim) + TypeConverter.ToNumber(rprim);
                            }
                        }

                        break;

                    case AssignmentOperator.MinusAssign:
                        lval = AreIntegerOperands(lval, rval)
                            ? JsNumber.Create(lval.AsInteger() - rval.AsInteger())
                            : JsNumber.Create(TypeConverter.ToNumber(lval) - TypeConverter.ToNumber(rval));
                        break;

                    case AssignmentOperator.TimesAssign:
                        if (AreIntegerOperands(lval, rval))
                        {
                            lval = (long) lval.AsInteger() * rval.AsInteger();
                        }
                        else if (lval.IsUndefined() || rval.IsUndefined())
                        {
                            lval = Undefined.Instance;
                        }
                        else
                        {
                            lval = TypeConverter.ToNumber(lval) * TypeConverter.ToNumber(rval);
                        }

                        break;

                    case AssignmentOperator.DivideAssign:
                        lval = Divide(lval, rval);
                        break;

                    case AssignmentOperator.ModuloAssign:
                        if (lval.IsUndefined() || rval.IsUndefined())
                        {
                            lval = Undefined.Instance;
                        }
                        else
                        {
                            lval = TypeConverter.ToNumber(lval) % TypeConverter.ToNumber(rval);
                        }

                        break;

                    case AssignmentOperator.BitwiseAndAssign:
                        lval = TypeConverter.ToInt32(lval) & TypeConverter.ToInt32(rval);
                        break;

                    case AssignmentOperator.BitwiseOrAssign:
                        lval = TypeConverter.ToInt32(lval) | TypeConverter.ToInt32(rval);
                        break;

                    case AssignmentOperator.BitwiseXOrAssign:
                        lval = TypeConverter.ToInt32(lval) ^ TypeConverter.ToInt32(rval);
                        break;

                    case AssignmentOperator.LeftShiftAssign:
                        lval = TypeConverter.ToInt32(lval) << (int) (TypeConverter.ToUint32(rval) & 0x1F);
                        break;

                    case AssignmentOperator.RightShiftAssign:
                        lval = TypeConverter.ToInt32(lval) >> (int) (TypeConverter.ToUint32(rval) & 0x1F);
                        break;

                    case AssignmentOperator.UnsignedRightShiftAssign:
                        lval = (uint) TypeConverter.ToInt32(lval) >> (int) (TypeConverter.ToUint32(rval) & 0x1F);
                        break;

                    default:
                        ExceptionHelper.ThrowNotImplementedException();
                        return null;
                }
            }

            _engine.PutValue(lref, lval);

            _engine._referencePool.Return(lref);
            return lval;
        }

        internal sealed class SimpleAssignmentExpression : JintExpression
        {
            private JintExpression _left;
            private JintExpression _right;

            private JintIdentifierExpression _leftIdentifier;
            private bool _evalOrArguments;

            public SimpleAssignmentExpression(Engine engine, AssignmentExpression expression) : base(engine, expression)
            {
                _initialized = false;
            }

            protected override void Initialize()
            {
                var assignmentExpression = ((AssignmentExpression) _expression);
                _left = Build(_engine, assignmentExpression.Left);
                _leftIdentifier = _left as JintIdentifierExpression;
                _evalOrArguments = _leftIdentifier?.HasEvalOrArguments == true;

                _right = Build(_engine, assignmentExpression.Right);
            }

            protected override object EvaluateInternal()
            {
                JsValue rval = null;
                if (_leftIdentifier != null)
                {
                    rval = AssignToIdentifier(_engine, _leftIdentifier, _right, _evalOrArguments);
                }
                return rval ?? SetValue();
            }

            private JsValue SetValue()
            {
                // slower version
                var lref = _left.Evaluate() as Reference;
                if (lref is null)
                {
                    ExceptionHelper.ThrowReferenceError(_engine.Realm, "not a valid reference");
                }
                lref.AssertValid(_engine);

                var rval = _right.GetValue();

                _engine.PutValue(lref, rval);
                _engine._referencePool.Return(lref);
                return rval;
            }

            internal static JsValue AssignToIdentifier(
                Engine engine,
                JintIdentifierExpression left,
                JintExpression right,
                bool hasEvalOrArguments)
            {
                var env = engine.ExecutionContext.LexicalEnvironment;
                var strict = StrictModeScope.IsStrictModeCode;
                if (JintEnvironment.TryGetIdentifierEnvironmentWithBindingValue(
                    engine,
                    env,
                    left._expressionName,
                    strict,
                    out var environmentRecord,
                    out _))
                {
                    if (strict && hasEvalOrArguments)
                    {
                        ExceptionHelper.ThrowSyntaxError(engine.Realm);
                    }

                    var rval = right.GetValue().Clone();

                    if (right._expression.IsFunctionWithName())
                    {
                        ((FunctionInstance) rval).SetFunctionName(left._expressionName.StringValue);
                    }

                    environmentRecord.SetMutableBinding(left._expressionName, rval, strict);
                    return rval;
                }

                return null;
            }
        }
    }
}