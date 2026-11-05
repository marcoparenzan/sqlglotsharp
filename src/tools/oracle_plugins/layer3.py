"""Reference Layer3 dialect: the base dialect plus the multi-word keyword MEMBER OF.

Mirrors the host's description: MEMBER OF is a multi-word keyword (TokenType.MEMBER_OF) and its range parser
builds JSONArrayContains(this=<left>, expression=<column>) using _parse_column for the right side.
"""
from sqlglot import exp
from sqlglot.dialects.dialect import Dialect
from sqlglot.parsers.base import BaseParser
from sqlglot.tokens import Tokenizer, TokenType


class Layer3Parser(BaseParser):
    RANGE_PARSERS = {
        **BaseParser.RANGE_PARSERS,
        TokenType.MEMBER_OF: lambda self, this: self.expression(
            exp.JSONArrayContains(this=this, expression=self._parse_column())
        ),
    }


class Layer3(Dialect):
    class Tokenizer(Tokenizer):
        KEYWORDS = {**Tokenizer.KEYWORDS, "MEMBER OF": TokenType.MEMBER_OF}

    Parser = Layer3Parser
