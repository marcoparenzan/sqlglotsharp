"""Record exact Decimal string/repr expectations without modifying upstream."""
from decimal import Decimal
import json
from pathlib import Path
values = ['0', '-0', '0.00', '-0.00', '1.2300', '001.2300', '.000001', '.0000001',
          '1e3', '1e-7', '12.30e5', '-1e1000000', '0e1000000', '0e-1000000',
          '123456789012345678901234567890.123456789', '١٢.٣٠', '_1__2_',
          'NaN', '-NaN42', 'sNaN001', 'inf', '-Infinity']
root = Path(__file__).resolve().parents[2]
cases = [{'input': value, 'str': str(Decimal(value)), 'repr': repr(Decimal(value))} for value in values]
(root / 'src/SqlGlotSharp.Tests/decimal-cases.json').write_text(json.dumps(cases, ensure_ascii=True, indent=2) + '\n', encoding='utf-8')
