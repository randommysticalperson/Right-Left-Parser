// =============================================================================
// InequalityParser.cs
//
// A C# Pratt (Top-Down Operator Precedence) Parser demonstrating:
//   1. yield return for lazy token streaming
//   2. Binding-power INEQUALITY as the sole mechanism for precedence & associativity
//   3. An "orientation-less" parser that works correctly both left-to-right
//      and right-to-left by flipping a single inequality
//
// Inspired by the viral puzzle: 16 / 4 * 2 = ?
//   Left-to-right  (standard PEMDAS/BODMAS): (16 / 4) * 2 = 8
//   Right-to-left  (alternative reading):     16 / (4 * 2) = 2
//
// Both answers are "correct" under their respective associativity conventions.
// =============================================================================

using System;
using System.Collections.Generic;

namespace InequalityParser
{
    // =========================================================================
    // TOKEN LAYER
    // =========================================================================

    public enum TokenKind
    {
        Number,
        Plus,       // +
        Minus,      // -
        Star,       // *
        Slash,      // /
        Caret,      // ^ (right-associative exponentiation)
        LParen,     // (
        RParen,     // )
        EOF
    }

    public class Token
    {
        public TokenKind Kind { get; private set; }
        public string Text { get; private set; }
        public int Position { get; private set; }

        public Token(TokenKind kind, string text, int position)
        {
            Kind = kind;
            Text = text;
            Position = position;
        }

        public override string ToString()
        {
            return "[" + Kind + " '" + Text + "' @" + Position + "]";
        }
    }

    // =========================================================================
    // LEXER — uses yield return for lazy, pull-based token streaming
    // =========================================================================
    //
    // yield return turns Tokenize() into a compiler-generated state machine.
    // Each MoveNext() call resumes execution from the last yield point.
    // Tokens are produced only when the parser demands them — truly lazy.

    public class Lexer
    {
        private readonly string _src;

        public Lexer(string source)
        {
            _src = source;
        }

        /// <summary>
        /// Lazily yields tokens one at a time using C# iterator blocks (yield return).
        /// The compiler transforms this method body into a hidden IEnumerator state machine.
        /// </summary>
        public IEnumerable<Token> Tokenize()
        {
            int i = 0;

            while (i < _src.Length)
            {
                // Skip whitespace
                if (char.IsWhiteSpace(_src[i]))
                {
                    i++;
                    continue;
                }

                // Numeric literal (integer or decimal)
                if (char.IsDigit(_src[i]))
                {
                    int start = i;
                    while (i < _src.Length && (char.IsDigit(_src[i]) || _src[i] == '.'))
                        i++;
                    yield return new Token(TokenKind.Number, _src.Substring(start, i - start), start);
                    continue;
                }

                // Single-character operators and parentheses
                TokenKind kind;
                switch (_src[i])
                {
                    case '+': kind = TokenKind.Plus;   break;
                    case '-': kind = TokenKind.Minus;  break;
                    case '*': kind = TokenKind.Star;   break;
                    case '/': kind = TokenKind.Slash;  break;
                    case '^': kind = TokenKind.Caret;  break;
                    case '(': kind = TokenKind.LParen; break;
                    case ')': kind = TokenKind.RParen; break;
                    default:
                        throw new ParseException("Unexpected character '" + _src[i] + "' at position " + i);
                }

                yield return new Token(kind, _src[i].ToString(), i);
                i++;
            }

            // Sentinel: always end the stream with an EOF token
            yield return new Token(TokenKind.EOF, "", _src.Length);
        }
    }

    // =========================================================================
    // AST NODES
    // =========================================================================

    public abstract class Expr
    {
        /// <summary>Pretty-print as a fully-parenthesised string.</summary>
        public abstract string Pretty();

        /// <summary>Evaluate the expression to a double.</summary>
        public abstract double Eval();
    }

    public class NumberExpr : Expr
    {
        public readonly double Value;

        public NumberExpr(double value)
        {
            Value = value;
        }

        public override string Pretty()
        {
            return Value.ToString("G");
        }

        public override double Eval()
        {
            return Value;
        }
    }

    public class BinaryExpr : Expr
    {
        public readonly Expr Left;
        public readonly Token Op;
        public readonly Expr Right;

        public BinaryExpr(Expr left, Token op, Expr right)
        {
            Left = left;
            Op = op;
            Right = right;
        }

        public override string Pretty()
        {
            return "(" + Left.Pretty() + " " + Op.Text + " " + Right.Pretty() + ")";
        }

        public override double Eval()
        {
            double l = Left.Eval();
            double r = Right.Eval();

            switch (Op.Kind)
            {
                case TokenKind.Plus:  return l + r;
                case TokenKind.Minus: return l - r;
                case TokenKind.Star:  return l * r;
                case TokenKind.Slash: return l / r;
                case TokenKind.Caret: return Math.Pow(l, r);
                default:
                    throw new ParseException("Unknown binary operator: " + Op.Text);
            }
        }
    }

    // =========================================================================
    // BINDING POWER
    // =========================================================================
    //
    // The central insight of Pratt parsing: every operator has two binding powers:
    //
    //   l_bp  — how strongly the operator grabs the operand to its LEFT
    //   r_bp  — how strongly the operator grabs the operand to its RIGHT
    //
    // The parser loop continues as long as:   l_bp >= min_bp   (the key INEQUALITY)
    //
    // Associativity is encoded entirely in the asymmetry between l_bp and r_bp:
    //
    //   Left-associative  (a / b * c = (a / b) * c):
    //       l_bp = base,     r_bp = base + 1
    //       The right recursive call requires strictly higher power,
    //       so equal-precedence operators do NOT nest to the right.
    //
    //   Right-associative (a ^ b ^ c = a ^ (b ^ c)):
    //       l_bp = base + 1, r_bp = base
    //       The right recursive call allows equal-precedence operators,
    //       so they nest to the right.
    //
    // The "orientation-less" trick: swap l_bp and r_bp for all operators
    // and the parser evaluates the same token stream right-to-left.

    public enum Associativity { Left, Right }

    public class BindingPower
    {
        public readonly int Left;
        public readonly int Right;

        public BindingPower(int left, int right)
        {
            Left = left;
            Right = right;
        }

        /// <summary>
        /// Build a BindingPower pair for a given base precedence and associativity.
        /// The asymmetry between Left and Right is the inequality that drives parsing.
        /// </summary>
        public static BindingPower For(int basePrecedence, Associativity assoc)
        {
            if (assoc == Associativity.Left)
                return new BindingPower(basePrecedence, basePrecedence + 1);  // l < r  → left-assoc
            else
                return new BindingPower(basePrecedence + 1, basePrecedence);  // l > r  → right-assoc
        }
    }

    // =========================================================================
    // PRATT PARSER — orientation-less via parameterised binding power
    // =========================================================================

    public class PrattParser
    {
        private readonly IEnumerator<Token> _iter;
        private Token _peek;

        /// <summary>
        /// When true:  all equal-precedence operators associate left-to-right (standard).
        /// When false: they associate right-to-left.
        /// This single flag flips the binding-power inequality for every operator.
        /// </summary>
        private readonly bool _leftToRight;

        public PrattParser(IEnumerable<Token> tokens, bool leftToRight = true)
        {
            _iter = tokens.GetEnumerator();
            _leftToRight = leftToRight;
            Consume(); // prime the lookahead
        }

        private Token Consume()
        {
            Token prev = _peek;
            _peek = _iter.MoveNext()
                ? _iter.Current
                : new Token(TokenKind.EOF, "", -1);
            return prev;
        }

        // -----------------------------------------------------------------
        // Binding power lookup
        // -----------------------------------------------------------------
        //
        // Precedence levels:
        //   10  →  + -
        //   20  →  * /
        //   30  →  ^ (always right-associative regardless of orientation flag)
        //
        // The _leftToRight flag inverts the asymmetry for +, -, *, /
        // but ^ is always right-associative (mathematical convention).

        private BindingPower InfixBP(TokenKind kind)
        {
            Associativity defaultAssoc = _leftToRight ? Associativity.Left : Associativity.Right;

            switch (kind)
            {
                case TokenKind.Plus:
                case TokenKind.Minus:
                    return BindingPower.For(10, defaultAssoc);

                case TokenKind.Star:
                case TokenKind.Slash:
                    return BindingPower.For(20, defaultAssoc);

                case TokenKind.Caret:
                    // ^ is always right-associative: a^b^c = a^(b^c)
                    return BindingPower.For(30, Associativity.Right);

                default:
                    return new BindingPower(0, 0); // not an infix operator
            }
        }

        // -----------------------------------------------------------------
        // Core parse loop
        // -----------------------------------------------------------------

        /// <summary>
        /// Parse an expression whose left-context has binding power <paramref name="minBP"/>.
        /// Recursion continues as long as the next operator's l_bp >= minBP.
        /// </summary>
        public Expr ParseExpr(int minBP = 0)
        {
            // --- NUD (null denotation): parse a prefix / atom ---
            Token token = Consume();
            Expr lhs;

            switch (token.Kind)
            {
                case TokenKind.Number:
                    lhs = new NumberExpr(double.Parse(token.Text));
                    break;

                case TokenKind.LParen:
                    lhs = ParseExpr(0); // parse inner expression at lowest precedence
                    if (_peek.Kind != TokenKind.RParen)
                        throw new ParseException("Expected closing ')'");
                    Consume(); // eat ')'
                    break;

                case TokenKind.Minus:
                    // Unary minus: right-binding power of 25 (between * and ^)
                    lhs = new BinaryExpr(new NumberExpr(0), token, ParseExpr(25));
                    break;

                default:
                    throw new ParseException("Unexpected token " + token + " in prefix position");
            }

            // --- LED (left denotation): parse infix operators ---
            while (true)
            {
                Token op = _peek;
                if (op.Kind == TokenKind.EOF || op.Kind == TokenKind.RParen)
                    break;

                BindingPower bp = InfixBP(op.Kind);

                // *** THE KEY INEQUALITY ***
                // If the operator's left binding power is less than the minimum
                // required by our caller, we stop and return lhs.
                //
                // For left-assoc:  l_bp == base, so equal-precedence operators stop here.
                // For right-assoc: l_bp == base+1, so equal-precedence operators continue.
                if (bp.Left < minBP)
                    break;

                Consume(); // eat the operator

                // Recurse with r_bp as the new minimum.
                // For left-assoc:  r_bp = base+1, recursive call stops before same-prec ops.
                // For right-assoc: r_bp = base,   recursive call CAN consume same-prec ops.
                Expr rhs = ParseExpr(bp.Right);

                lhs = new BinaryExpr(lhs, op, rhs);
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
            string direction = leftToRight ? "Left-to-Right (LTR)" : "Right-to-Left (RTL)";
            Console.WriteLine("  Direction : " + direction);

            var lexer  = new Lexer(expression);
            var parser = new PrattParser(lexer.Tokenize(), leftToRight);
            var ast    = parser.ParseExpr();

            Console.WriteLine("  AST       : " + ast.Pretty());
            Console.WriteLine("  Result    : " + ast.Eval());
        }

        static void Demonstrate(string expression)
        {
            Console.WriteLine("+---------------------------------------------------------");
            Console.WriteLine("|  Expression: " + expression);
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
            Console.WriteLine("  Inequality-Based Orientation-Less Pratt Parser Demo");
            Console.WriteLine("  Inspired by the viral puzzle: 16 / 4 * 2 = ?");
            Console.WriteLine("=============================================================");
            Console.WriteLine();
            Console.WriteLine("  The Pinterest pin shows the expression  16 / 4 * 2");
            Console.WriteLine("  and two camps of commenters:");
            Console.WriteLine("    Tom: 16/4*2 = (16/4)*2 = 8   [left-to-right]");
            Console.WriteLine("    Lea: 4*2=8, 16/8 = 2         [right-to-left]");
            Console.WriteLine();

            // The Pinterest puzzle: 16 / 4 * 2
            Demonstrate("16 / 4 * 2");

            // Classic PEMDAS/BODMAS test: multiplication before addition
            Demonstrate("2 + 3 * 4");

            // Right-associative exponentiation: 2^3^2
            // Standard math: 2^(3^2) = 2^9 = 512 (right-assoc regardless of orientation)
            Demonstrate("2 ^ 3 ^ 2");

            // Subtraction chain: 10 - 3 - 2
            // LTR: (10-3)-2 = 5  |  RTL: 10-(3-2) = 9
            Demonstrate("10 - 3 - 2");

            // Parentheses always override orientation
            Demonstrate("(10 - 3) - 2");

            // Longer chain: 9 / 3 * (2 + 1)
            Demonstrate("9 / 3 * (2 + 1)");

            Console.WriteLine("=============================================================");
            Console.WriteLine("  Binding Power Inequality Summary");
            Console.WriteLine("=============================================================");
            Console.WriteLine();
            Console.WriteLine("  For LEFT-associative operators (LTR mode):");
            Console.WriteLine("    l_bp = base,     r_bp = base + 1");
            Console.WriteLine("    Loop condition: l_bp >= min_bp");
            Console.WriteLine("    => equal-precedence ops do NOT recurse right  => left-assoc");
            Console.WriteLine();
            Console.WriteLine("  For RIGHT-associative operators (RTL mode):");
            Console.WriteLine("    l_bp = base + 1, r_bp = base");
            Console.WriteLine("    Loop condition: l_bp >= min_bp");
            Console.WriteLine("    => equal-precedence ops DO recurse right      => right-assoc");
            Console.WriteLine();
            Console.WriteLine("  The single boolean _leftToRight flips this inequality");
            Console.WriteLine("  for ALL operators simultaneously, making the parser");
            Console.WriteLine("  truly orientation-less.");
            Console.WriteLine("=============================================================");
        }
    }
}
