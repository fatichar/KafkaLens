using System;
using System.Text;

namespace KafkaLens.ViewModels.Search;

public class SearchParser
{
    private enum TokenType
    {
        Not,
        And,
        Or,
        OpenParen,
        CloseParen,
        Term,
        End
    }

    private record Token(TokenType Type, string Value = "");

    private readonly string input;
    private int position;

    public SearchParser(string input)
    {
        this.input = input ?? "";
        this.position = 0;
    }

    public static IFilterExpression Parse(string input, bool defaultMatch = true)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return defaultMatch ? new AllMatchExpression() : (IFilterExpression)new NoneMatchExpression();
        }
        return new SearchParser(input).ParseExpression();
    }

    private IFilterExpression ParseExpression()
    {
        return ParseOrExpression();
    }

    // OrExpression -> AndExpression ( ( '||' | <implicit> ) AndExpression )*
    private IFilterExpression ParseOrExpression()
    {
        var left = ParseAndExpression();

        while (true)
        {
            var token = PeekToken();
            if (token.Type == TokenType.Or)
            {
                NextToken(); // consume '||'
                var right = ParseAndExpression();
                left = new OrExpression(left, right);
            }
            else if (IsTermStart(token))
            {
                // Implicit OR
                var right = ParseAndExpression();
                left = new OrExpression(left, right);
            }
            else
            {
                break;
            }
        }

        return left;
    }

    // AndExpression -> NotExpression ( '&&' NotExpression )*
    private IFilterExpression ParseAndExpression()
    {
        var left = ParseNotExpression();

        while (PeekToken().Type == TokenType.And)
        {
            NextToken(); // consume '&&'
            var right = ParseNotExpression();
            left = new AndExpression(left, right);
        }

        return left;
    }

    // NotExpression -> '!' NotExpression | PrimaryExpression
    private IFilterExpression ParseNotExpression()
    {
        if (PeekToken().Type == TokenType.Not)
        {
            NextToken(); // consume '!'
            return new NotExpression(ParseNotExpression());
        }
        return ParsePrimaryExpression();
    }

    // PrimaryExpression -> Term | '(' Expression ')'
    private IFilterExpression ParsePrimaryExpression()
    {
        var token = NextToken();
        if (token.Type == TokenType.Term)
        {
            return new TermExpression(token.Value);
        }
        if (token.Type == TokenType.OpenParen)
        {
            var expr = ParseExpression();
            if (NextToken().Type != TokenType.CloseParen)
            {
                // Unmatched paren, just treat as end
            }
            return expr;
        }

        return new AllMatchExpression(); // Fallback
    }

    private bool IsTermStart(Token token)
    {
        return token.Type == TokenType.Term || token.Type == TokenType.OpenParen || token.Type == TokenType.Not;
    }

    private Token? peekedToken;

    private Token PeekToken()
    {
        return peekedToken ??= ReadToken();
    }

    private Token NextToken()
    {
        var token = PeekToken();
        peekedToken = null;
        return token;
    }

    private Token ReadToken()
    {
        SkipWhitespace();

        if (position >= input.Length)
        {
            return new Token(TokenType.End);
        }

        char c = input[position];
        if (c == '!')
        {
            position++;
            return new Token(TokenType.Not);
        }
        if (c == '&' && position + 1 < input.Length && input[position + 1] == '&')
        {
            position += 2;
            return new Token(TokenType.And);
        }
        if (c == '|' && position + 1 < input.Length && input[position + 1] == '|')
        {
            position += 2;
            return new Token(TokenType.Or);
        }
        if (c == '(')
        {
            position++;
            return new Token(TokenType.OpenParen);
        }
        if (c == ')')
        {
            position++;
            return new Token(TokenType.CloseParen);
        }
        if (c == '"')
        {
            return ReadQuotedTerm();
        }

        return ReadTerm();
    }

    private Token ReadQuotedTerm()
    {
        position++; // consume opening quote
        var sb = new StringBuilder();
        while (position < input.Length && input[position] != '"')
        {
            sb.Append(input[position]);
            position++;
        }
        if (position < input.Length)
        {
            position++; // consume closing quote
        }
        return new Token(TokenType.Term, sb.ToString());
    }

    private Token ReadTerm()
    {
        var sb = new StringBuilder();
        if (position < input.Length && IsOperatorChar(input[position]))
        {
            sb.Append(input[position]);
            position++;
        }
        else
        {
            while (position < input.Length && !char.IsWhiteSpace(input[position]) && !IsOperatorChar(input[position]))
            {
                sb.Append(input[position]);
                position++;
            }
        }
        return new Token(TokenType.Term, sb.ToString());
    }

    private bool IsOperatorChar(char c)
    {
        return c == '!' || c == '&' || c == '|' || c == '(' || c == ')';
    }

    private void SkipWhitespace()
    {
        while (position < input.Length && char.IsWhiteSpace(input[position]))
        {
            position++;
        }
    }
}
