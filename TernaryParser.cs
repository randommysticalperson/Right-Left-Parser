// =============================================================================
// TernaryParser.cs
//
// Extends the inequality-based orientation-less Pratt parser to support the
// TERNARY OPERATOR  (condition ? thenExpr : elseExpr)  as a first-class
// parsed and evaluated expression.
//
// Key insight: the ternary operator is a MIXFIX operator — it has three
// operands and two punctuation tokens (? and :).  In a Pratt parser it is
// handled as an infix operator whose LED (left denotation) consumes the
// middle operand by calling ParseExpr(0) and then expects the ':' separator,
// after which it consumes the right operand.
//
// The binding-power inequality still governs everything:
//
//   condition ? thenExpr : elseExpr
//
//   l_bp = 3,  r_bp = 2   (right-associative by convention, like C/C++/C#)
//
//   This means:
//     a ? b : c ? d : e   parses as   a ? b : (c ? d : e)
//   which is the standard right-associative ternary nesting.
//
// The orientation flag still works: setting leftToRight=false makes the
// arithmetic sub-expressions inside the ternary evaluate right-to-left,
// while the ternary itself remains right-associative (it has no meaningful
// left-associative form).
//
// Supported grammar:
//   expr  ::= number
//           | '(' expr ')'
//           | '-' expr
//           | expr '+' expr
//           | expr '-' expr
//           | expr '*' expr
//           | expr '/' expr
//           | expr '^' expr
//           | expr '>' expr
//           | expr '<' expr
//           | expr '==' expr
//           | expr '!=' expr
//           | expr '>=' expr
//           | expr '<=' expr
//           | expr '?' expr ':' expr    ← ternary
// =============================================================================

using System;
using System.Collections.Generic;

namespace TernaryParser
{
    // =========================================================================
    // TOKEN LAYER
    // =========================================================================

    public enum TokenKind
    {
        Number,
        Plus, Minus, Star, Slash, Caret,
        Gt, Lt, GtEq, LtEq, EqEq, BangEq,   // comparison operators
        Question,   // ?
        Colon,      // :
        LParen, RParen,
        EOF
    }

    public class Token
    {
        public TokenKind Kind { get; private set; }
        public string Text { get; private set; }
        public int Position { get; private set; }

        public Token(TokenKind kind, string text, int position)
        {
            Kind = kind; Text = text; Position = position;
        }

        public override string ToString()
        {
            return "[" + Kind + " '" + Text + "' @" + Position + "]";
        }
    }

    // =========================================================================
    // LEXER  (yield return — lazy token stream)
    // =========================================================================

    public class Lexer
    {
        private readonly string _src;
        public Lexer(string source) { _src = source; }

        /// <summary>
        /// Lazily yields tokens using C# iterator blocks (yield return).
        /// The compiler generates a hidden IEnumerator state machine so that
        /// each MoveNext() resumes from the last yield point.
        /// </summary>
        public IEnumerable<Token> Tokenize()
        {
            int i = 0;
            while (i < _src.Length)
            {
                if (char.IsWhiteSpace(_src[i])) { i++; continue; }

                // Numbers
                if (char.IsDigit(_src[i]))
                {
                    int start = i;
                    while (i < _src.Length && (char.IsDigit(_src[i]) || _src[i] == '.'))
                        i++;
                    yield return new Token(TokenKind.Number, _src.Substring(start, i - start), start);
                    continue;
                }

                // Two-character tokens first
                if (i + 1 < _src.Length)
                {
                    string two = _src.Substring(i, 2);
                    TokenKind tk2;
                    bool matched = true;
                    switch (two)
                    {
                        case ">=": tk2 = TokenKind.GtEq;   break;
                        case "<=": tk2 = TokenKind.LtEq;   break;
                        case "==": tk2 = TokenKind.EqEq;   break;
                        case "!=": tk2 = TokenKind.BangEq; break;
                        default: tk2 = TokenKind.EOF; matched = false; break;
                    }
                    if (matched)
                    {
                        yield return new Token(tk2, two, i);
                        i += 2;
                        continue;
                    }
                }

                // Single-character tokens
                TokenKind kind;
                switch (_src[i])
                {
                    case '+': kind = TokenKind.Plus;     break;
                    case '-': kind = TokenKind.Minus;    break;
                    case '*': kind = TokenKind.Star;     break;
                    case '/': kind = TokenKind.Slash;    break;
                    case '^': kind = TokenKind.Caret;    break;
                    case '>': kind = TokenKind.Gt;       break;
                    case '<': kind = TokenKind.Lt;       break;
                    case '?': kind = TokenKind.Question; break;
                    case ':': kind = TokenKind.Colon;    break;
                    case '(': kind = TokenKind.LParen;   break;
                    case ')': kind = TokenKind.RParen;   break;
                    default:
                        throw new ParseException(
                            "Unexpected character '" + _src[i] + "' at position " + i);
                }
                yield return new Token(kind, _src[i].ToString(), i);
                i++;
            }
            yield return new Token(TokenKind.EOF, "", _src.Length);
        }
    }

    // =========================================================================
    // AST NODES
    // =========================================================================

    public abstract class Expr
    {
        public abstract string Pretty();
        public abstract double Eval();
    }

    public class NumberExpr : Expr
    {
        public readonly double Value;
        public NumberExpr(double value) { Value = value; }
        public override string Pretty() { return Value.ToString("G"); }
        public override double Eval() { return Value; }
    }

    public class BinaryExpr : Expr
    {
        public readonly Expr Left;
        public readonly Token Op;
        public readonly Expr Right;

        public BinaryExpr(Expr left, Token op, Expr right)
        {
            Left = left; Op = op; Right = right;
        }

        public override string Pretty()
        {
            return "(" + Left.Pretty() + " " + Op.Text + " " + Right.Pretty() + ")";
        }

        public override double Eval()
        {
            double l = Left.Eval(), r = Right.Eval();
            switch (Op.Kind)
            {
                case TokenKind.Plus:   return l + r;
                case TokenKind.Minus:  return l - r;
                case TokenKind.Star:   return l * r;
                case TokenKind.Slash:  return l / r;
                case TokenKind.Caret:  return Math.Pow(l, r);
                case TokenKind.Gt:     return l > r  ? 1 : 0;
                case TokenKind.Lt:     return l < r  ? 1 : 0;
                case TokenKind.GtEq:   return l >= r ? 1 : 0;
                case TokenKind.LtEq:   return l <= r ? 1 : 0;
                case TokenKind.EqEq:   return Math.Abs(l - r) < 1e-12 ? 1 : 0;
                case TokenKind.BangEq: return Math.Abs(l - r) >= 1e-12 ? 1 : 0;
                default:
                    throw new ParseException("Unknown binary operator: " + Op.Text);
            }
        }
    }

    // =========================================================================
    // TERNARY AST NODE
    // =========================================================================
    //
    // The ternary node holds three sub-expressions:
    //   Condition  — evaluated first; non-zero is truthy
    //   Then       — returned if Condition is truthy
    //   Else       — returned if Condition is falsy
    //
    // This is the only node in the tree that branches on a runtime value.

    public class TernaryExpr : Expr
    {
        public readonly Expr Condition;
        public readonly Expr Then;
        public readonly Expr Else;

        public TernaryExpr(Expr condition, Expr then, Expr els)
        {
            Condition = condition; Then = then; Else = els;
        }

        public override string Pretty()
        {
            return "(" + Condition.Pretty() + " ? " + Then.Pretty() + " : " + Else.Pretty() + ")";
        }

        public override double Eval()
        {
            // Non-zero condition is truthy (same convention as C/C++/C#)
            double cond = Condition.Eval();
            return cond != 0 ? Then.Eval() : Else.Eval();
        }
    }

    // =========================================================================
    // BINDING POWER
    // =========================================================================

    public enum Associativity { Left, Right }

    public class BindingPower
    {
        public readonly int Left;
        public readonly int Right;

        public BindingPower(int left, int right) { Left = left; Right = right; }

        public static BindingPower For(int basePrecedence, Associativity assoc)
        {
            if (assoc == Associativity.Left)
                return new BindingPower(basePrecedence, basePrecedence + 1);
            else
                return new BindingPower(basePrecedence + 1, basePrecedence);
        }
    }

    // =========================================================================
    // PRATT PARSER — with ternary operator support
    // =========================================================================
    //
    // Precedence table (low → high):
    //
    //   Precedence  Operators         Associativity
    //   ----------  --------          -------------
    //    3          ?  :  (ternary)   Right
    //    5          == !=             Left  (or Right in RTL mode)
    //    7          >  <  >= <=       Left  (or Right in RTL mode)
    //   10          +  -              Left  (or Right in RTL mode)
    //   20          *  /              Left  (or Right in RTL mode)
    //   30          ^                 Right (always)
    //
    // The ternary operator is special: it is RIGHT-associative regardless of
    // the orientation flag, because left-associative ternary has no standard
    // meaning and would be confusing.  Its l_bp=4 and r_bp=3 (right-assoc).
    //
    // The '?' token acts as the infix trigger; the ':' is consumed inside the
    // LED handler as a mandatory separator between the then and else branches.

    public class PrattParser
    {
        private readonly IEnumerator<Token> _iter;
        private Token _peek;
        private readonly bool _leftToRight;

        public PrattParser(IEnumerable<Token> tokens, bool leftToRight = true)
        {
            _iter = tokens.GetEnumerator();
            _leftToRight = leftToRight;
            Consume();
        }

        private Token Consume()
        {
            Token prev = _peek;
            _peek = _iter.MoveNext()
                ? _iter.Current
                : new Token(TokenKind.EOF, "", -1);
            return prev;
        }

        private Token Expect(TokenKind kind)
        {
            if (_peek.Kind != kind)
                throw new ParseException(
                    "Expected " + kind + " but got " + _peek);
            return Consume();
        }

        // -----------------------------------------------------------------
        // Binding power lookup
        // -----------------------------------------------------------------

        private BindingPower InfixBP(TokenKind kind)
        {
            Associativity def = _leftToRight ? Associativity.Left : Associativity.Right;

            switch (kind)
            {
                // Ternary: always right-associative so nested ternaries chain correctly
                case TokenKind.Question:
                    return new BindingPower(4, 3);   // l_bp=4, r_bp=3 → right-assoc

                case TokenKind.EqEq:
                case TokenKind.BangEq:
                    return BindingPower.For(5, def);

                case TokenKind.Gt:
                case TokenKind.Lt:
                case TokenKind.GtEq:
                case TokenKind.LtEq:
                    return BindingPower.For(7, def);

                case TokenKind.Plus:
                case TokenKind.Minus:
                    return BindingPower.For(10, def);

                case TokenKind.Star:
                case TokenKind.Slash:
                    return BindingPower.For(20, def);

                case TokenKind.Caret:
                    return BindingPower.For(30, Associativity.Right);

                default:
                    return new BindingPower(0, 0);
            }
        }

        // -----------------------------------------------------------------
        // Core parse loop
        // -----------------------------------------------------------------

        public Expr ParseExpr(int minBP = 0)
        {
            // NUD — prefix / atom
            Token token = Consume();
            Expr lhs;

            switch (token.Kind)
            {
                case TokenKind.Number:
                    lhs = new NumberExpr(double.Parse(token.Text));
                    break;

                case TokenKind.LParen:
                    lhs = ParseExpr(0);
                    Expect(TokenKind.RParen);
                    break;

                case TokenKind.Minus:
                    lhs = new BinaryExpr(new NumberExpr(0), token, ParseExpr(25));
                    break;

                default:
                    throw new ParseException(
                        "Unexpected token " + token + " in prefix position");
            }

            // LED — infix operators
            while (true)
            {
                Token op = _peek;
                if (op.Kind == TokenKind.EOF
                    || op.Kind == TokenKind.RParen
                    || op.Kind == TokenKind.Colon)   // ':' closes a ternary branch
                    break;

                BindingPower bp = InfixBP(op.Kind);

                // THE KEY INEQUALITY — same as before
                if (bp.Left < minBP)
                    break;

                Consume(); // eat the operator

                // -------------------------------------------------------
                // TERNARY OPERATOR  (mixfix: condition ? then : else)
                // -------------------------------------------------------
                if (op.Kind == TokenKind.Question)
                {
                    // Parse the THEN branch at lowest precedence (0).
                    // The ':' will stop the inner ParseExpr because Colon
                    // is in the break-list above.
                    Expr thenExpr = ParseExpr(0);

                    // Consume the mandatory ':' separator
                    Expect(TokenKind.Colon);

                    // Parse the ELSE branch with r_bp=3 (right-assoc nesting)
                    Expr elseExpr = ParseExpr(bp.Right);

                    lhs = new TernaryExpr(lhs, thenExpr, elseExpr);
                }
                else
                {
                    // Normal binary operator
                    Expr rhs = ParseExpr(bp.Right);
                    lhs = new BinaryExpr(lhs, op, rhs);
                }
            }

            return lhs;
        }
    }

    // =========================================================================
    // EXCEPTION
    // =========================================================================

    public class ParseException : Exception
    {
        public ParseException(string message) : base(message) { }
    }

    // =========================================================================
    // DEMO PROGRAM
    // =========================================================================

    class Program
    {
        static void RunDemo(string expression, bool leftToRight)
        {
            string dir = leftToRight ? "LTR" : "RTL";
            var lexer  = new Lexer(expression);
            var parser = new PrattParser(lexer.Tokenize(), leftToRight);
            var ast    = parser.ParseExpr();
            double result = ast.Eval();
            Console.WriteLine("  [" + dir + "]  AST    : " + ast.Pretty());
            Console.WriteLine("  [" + dir + "]  Result : " + result);
        }

        static void Demonstrate(string expression, string note = "")
        {
            Console.WriteLine("+---------------------------------------------------------");
            if (note != "")
                Console.WriteLine("|  // " + note);
            Console.WriteLine("|  " + expression);
            Console.WriteLine("+---------------------------------------------------------");
            RunDemo(expression, leftToRight: true);
            Console.WriteLine("|");
            RunDemo(expression, leftToRight: false);
            Console.WriteLine("+---------------------------------------------------------");
            Console.WriteLine();
        }

        static void Main(string[] args)
        {
            Console.WriteLine("=============================================================");
            Console.WriteLine("  Ternary-Extended Orientation-Less Pratt Parser");
            Console.WriteLine("  Evaluation IS the ternary operator");
            Console.WriteLine("=============================================================");
            Console.WriteLine();

            // ------------------------------------------------------------------
            // 1. The Pinterest puzzle resolved by ternary
            //    Instead of arguing about 16/4*2, we let the ternary decide:
            //    (16/4*2 == 8) ? 8 : 2
            //    LTR: (16/4)*2 = 8, condition true  → 8
            //    RTL: 16/(4*2) = 2, condition false → 2
            // ------------------------------------------------------------------
            Demonstrate(
                "16 / 4 * 2 == 8 ? 8 : 2",
                "Pinterest puzzle: LTR gives 8, RTL gives 2");

            // ------------------------------------------------------------------
            // 2. Simple ternary: condition is a comparison
            // ------------------------------------------------------------------
            Demonstrate(
                "3 > 2 ? 100 : 200",
                "3 > 2 is true (1), so result is 100");

            // ------------------------------------------------------------------
            // 3. Ternary with arithmetic in all three branches
            // ------------------------------------------------------------------
            Demonstrate(
                "2 + 3 > 4 ? 10 * 2 : 10 / 2",
                "2+3=5 > 4 is true → 10*2=20 (LTR), 10/2=5 (LTR)");

            // ------------------------------------------------------------------
            // 4. Nested ternary (right-associative chaining)
            //    a ? b : c ? d : e  parses as  a ? b : (c ? d : e)
            // ------------------------------------------------------------------
            Demonstrate(
                "0 ? 1 : 0 ? 2 : 3",
                "Nested ternary: 0?1:(0?2:3) → 0?2:3 → 3");

            // ------------------------------------------------------------------
            // 5. Ternary condition uses == comparison
            // ------------------------------------------------------------------
            Demonstrate(
                "16 / 4 * 2 == 2 ? 42 : 99",
                "LTR: 16/4*2=8 != 2 → 99 | RTL: 16/(4*2)=2 == 2 → 42");

            // ------------------------------------------------------------------
            // 6. Ternary inside arithmetic
            // ------------------------------------------------------------------
            Demonstrate(
                "10 + (1 > 0 ? 5 : 0) * 3",
                "1>0 true → 10 + 5*3 = 25");

            // ------------------------------------------------------------------
            // 7. Chained comparisons in condition
            // ------------------------------------------------------------------
            Demonstrate(
                "2 ^ 3 ^ 2 == 512 ? 512 : -1",
                "2^(3^2)=512 always (^ is always right-assoc) → 512");

            Console.WriteLine("=============================================================");
            Console.WriteLine("  How the Ternary Operator Fits the Inequality Framework");
            Console.WriteLine("=============================================================");
            Console.WriteLine();
            Console.WriteLine("  The ternary '?' is an infix operator with:");
            Console.WriteLine("    l_bp = 4,  r_bp = 3   (right-associative)");
            Console.WriteLine();
            Console.WriteLine("  When the parser sees '?':");
            Console.WriteLine("    1. l_bp=4 >= min_bp  → enter LED handler");
            Console.WriteLine("    2. ParseExpr(0)      → parse THEN branch (stops at ':')");
            Console.WriteLine("    3. Expect(':')       → consume the separator");
            Console.WriteLine("    4. ParseExpr(r_bp=3) → parse ELSE branch");
            Console.WriteLine("       (r_bp=3 means another '?' with l_bp=4 CAN nest here");
            Console.WriteLine("        → right-associative chaining)");
            Console.WriteLine();
            Console.WriteLine("  The orientation flag (_leftToRight) still flips the");
            Console.WriteLine("  binding powers of +, -, *, / inside the branches,");
            Console.WriteLine("  so the arithmetic sub-expressions evaluate in the");
            Console.WriteLine("  chosen direction while the ternary structure itself");
            Console.WriteLine("  remains right-associative.");
            Console.WriteLine("=============================================================");
        }
    }
}
