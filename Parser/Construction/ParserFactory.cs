using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Piglet.Configuration;

namespace Piglet.Construction
{
    public class ParserFactory<T>
    {
        private readonly IGrammar<T> grammar;

        public ParserFactory(IGrammar<T> grammar)
        {
            this.grammar = grammar;
        }

        public IParser<T> CreateParser()
        {
            // First order of business is to create the canonical list of LR0 states.
            // This starts with augmenting the grammar with an accept symbol, then we derive the
            // grammar from that
            var start = grammar.Start;

            // Get the first and follow sets for all nonterminal symbols
            var first = CalculateFirst();
            var follow = CalculateFollow(first);

            // So, we are going to calculate the LR0 closure for the start symbol, which should
            // be the augmented accept state of the grammar.
            // The closure is all states which are accessible by the dot at the left hand side of the
            // item.
            var itemSets = new List<List<Lr0Item<T>>>
                               {
                                   Closure(new List<Lr0Item<T>>
                                               {
                                                   new Lr0Item<T>(start, 0)
                                               })
                               };
            
            // TODO: This method is probably one big stupid performance sink since it iterates WAY to many times over the input

            // Repeat until nothing gets added any more
            while (true)
            {
                bool anythingAdded = false;

                foreach (var itemSet in itemSets)
                {
                    foreach (var symbol in grammar.AllSymbols)
                    {
                        // Calculate the itemset for by goto for each symbol in the grammar
                        var gotoSet = Goto(itemSet, symbol).ToList();

                        // If there is anything found in the set
                        if (gotoSet.Any())
                        {
                            // Do a closure on the goto set and see if it's already present in the sets of items that we have
                            // if that is not the case add it to the item sets and restart the entire thing.
                            gotoSet = Closure(gotoSet);
                            if (!itemSets.Any(f => f.All(a => gotoSet.Any(b => b.ProductionRule == a.ProductionRule && 
                                                                               b.DotLocation == a.DotLocation))))
                            {
                                itemSets.Add(gotoSet);
                                anythingAdded = true;
                                break;
                            }
                        }
                    }
                    if (anythingAdded)
                        break;
                }
                if (!anythingAdded)
                    break;
            }

            return null;
        }

        private TerminalSet<T> CalculateFollow(TerminalSet<T> first)
        {
            var follow = new TerminalSet<T>(grammar);

            // As per the dragon book, add end-of-input token to 
            // follow on the start symbol
            follow.Add(grammar.AcceptSymbol, grammar.EndOfInputTerminal);

            // TODO: This doesn't support epsilon rules yet
            bool addedThings;
            do
            {
                addedThings = false;
                foreach (var productionRule in grammar.ProductionRules)
                {
                    for (int n = 0; n < productionRule.Symbols.Length; ++n)
                    {
                        // Skip all terminals
                        if (productionRule.Symbols[n] is NonTerminal<T>)
                        {
                            var currentSymbol = (NonTerminal<T>) productionRule.Symbols[n];
                            var nextSymbol = n == productionRule.Symbols.Length - 1
                                                 ? null
                                                 : productionRule.Symbols[n + 1];
                            if (nextSymbol == null)
                            {
                                // Add everything in FOLLOW(production.ResultSymbol) since we were at the end
                                // of the production
                                // TODO: This is also a valid action if there is an Epsilon production of nextsymbol
                                foreach (var terminal in follow[(NonTerminal<T>)productionRule.ResultSymbol])
                                {
                                    addedThings |= follow.Add(currentSymbol, terminal);
                                }
                            }

                            if (nextSymbol != null)
                            {
                                // It's not at the end, if the next symbol is a terminal, just add it
                                if (nextSymbol is Terminal<T>)
                                {
                                    addedThings |= follow.Add(currentSymbol, (Terminal<T>) nextSymbol);
                                }
                                else
                                {
                                    // Add everthing in FIRST(nextSymbol)
                                    foreach (var terminal in first[(NonTerminal<T>)nextSymbol])
                                    {
                                        addedThings |= follow.Add(currentSymbol, terminal);
                                    }
                                }
                            }
                        }
                    }
                }
            } while (addedThings);

            return follow;
        }

        private TerminalSet<T> CalculateFirst()
        {
            var first = new TerminalSet<T>(grammar);

            // Algorithm is that if a nonterminal has a production that starts with a 
            // terminal, we add that to the first set. If it starts with a nonterminal, we add
            // that nonterminals firsts to the known firsts of our nonterminal.
            // TODO: There is probably performance benefits to optimizing this.
            bool addedThings;
            do
            {
                addedThings = false;
                
                foreach (var symbol in grammar.AllSymbols.OfType<NonTerminal<T>>())
                {
                    foreach (var productionRule in symbol.ProductionRules)
                    {
                        foreach (var productionSymbol in productionRule.Symbols)
                        {
                            // Terminals are trivial, just add them
                            if (productionSymbol is Terminal<T>)
                            {
                                addedThings |= first.Add(symbol, (Terminal<T>) productionSymbol);
                                
                                // This production rule is done now
                                break;
                            }

                            if (productionSymbol is NonTerminal<T>)
                            {
                                var nonTerminal = (NonTerminal<T>) productionSymbol;
                                // TODO: The check for nullable should be here...
                                // TODO: if it is nullable, it should add Epsilon to the first
                                // TODO: and continue with the next one. We are going to assume nullable
                                // TODO: is false and go on
                                var nullable = false;
                                if (nullable)
                                {
                                    throw new NotImplementedException("Nullable production rules doesn't work yet");
                                }
                                else
                                {
                                    foreach (var f in first[nonTerminal])
                                    {
                                        addedThings |= first.Add(symbol, f);
                                    }
                                    // Jump out since the other symbols are not in the first set
                                    break;
                                }
                            }
                        }
                    }
                }
            } while (addedThings); 

            return first;
        }

        private IEnumerable<Lr0Item<T>> Goto(IEnumerable<Lr0Item<T>> closures, ISymbol<T> symbol)
        {
            // Every place there is a symbol to the right of the dot that matches the symbol we are looking for
            // add a new Lr0 item that has the dot moved one step to the right.
            return from lr0Item in closures
                   where lr0Item.SymbolRightOfDot != null && lr0Item.SymbolRightOfDot == symbol
                   select new Lr0Item<T>(lr0Item.ProductionRule, lr0Item.DotLocation + 1);
        }

        private List<Lr0Item<T>> Closure(IEnumerable<Lr0Item<T>> items)
        {
            // The items themselves are always in their own closure set
            var closure = new List<Lr0Item<T>>();
            closure.AddRange(items);

            var added = new HashSet<ISymbol<T>>();
            while (true)
            {
                var toAdd = new List<Lr0Item<T>>();
                foreach (var item in closure)
                {
                    ISymbol<T> symbolRightOfDot = item.SymbolRightOfDot;
                    if (symbolRightOfDot != null && !added.Contains(symbolRightOfDot))
                    {
                        // Create new Lr0 items from all rules where the resulting symbol of the production rule
                        // matches the symbol that was to the right of the dot.
                        toAdd.AddRange(
                            grammar.ProductionRules.Where(f => f.ResultSymbol == symbolRightOfDot).Select(
                                f => new Lr0Item<T>(f, 0)));
                        added.Add(symbolRightOfDot);
                    }
                }

                if (!toAdd.Any())
                    break;
                closure.AddRange(toAdd);
            }

            return closure;
        }
    }
}

