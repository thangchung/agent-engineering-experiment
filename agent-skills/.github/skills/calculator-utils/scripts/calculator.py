#!/usr/bin/env python3
"""
Calculator Utilities - Basic arithmetic operations.

Usage:
    python calculator.py <operation> <numbers...>

Operations:
    add       - Add all numbers together
    subtract  - Subtract numbers from the first number
    multiply  - Multiply all numbers together
    divide    - Divide first number by subsequent numbers

Examples:
    python calculator.py add 5 3
    python calculator.py subtract 10 4
    python calculator.py multiply 6 7
    python calculator.py divide 20 4
"""

import sys
from typing import List
from functools import reduce


def add(*numbers: float) -> float:
    """Add all numbers together."""
    return sum(numbers)


def subtract(*numbers: float) -> float:
    """Subtract all subsequent numbers from the first number."""
    if len(numbers) == 0:
        return 0
    if len(numbers) == 1:
        return numbers[0]
    return reduce(lambda a, b: a - b, numbers)


def multiply(*numbers: float) -> float:
    """Multiply all numbers together."""
    if len(numbers) == 0:
        return 0
    return reduce(lambda a, b: a * b, numbers)


def divide(*numbers: float) -> float:
    """Divide the first number by all subsequent numbers."""
    if len(numbers) == 0:
        return 0
    if len(numbers) == 1:
        return numbers[0]
    
    for num in numbers[1:]:
        if num == 0:
            raise ValueError("Division by zero is not allowed")
    
    return reduce(lambda a, b: a / b, numbers)


def main():
    if len(sys.argv) < 3:
        print("Error: Insufficient arguments")
        print("Usage: python calculator.py <operation> <numbers...>")
        print("Operations: add, subtract, multiply, divide")
        sys.exit(1)
    
    operation = sys.argv[1].lower()
    
    try:
        numbers = [float(n) for n in sys.argv[2:]]
    except ValueError as e:
        print(f"Error: Invalid number format - {e}")
        sys.exit(1)
    
    operations = {
        "add": add,
        "subtract": subtract,
        "multiply": multiply,
        "divide": divide
    }
    
    if operation not in operations:
        print(f"Error: Unknown operation '{operation}'")
        print("Valid operations: add, subtract, multiply, divide")
        sys.exit(1)
    
    try:
        result = operations[operation](*numbers)
        print(result)
    except ValueError as e:
        print(f"Error: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()
