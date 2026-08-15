// xunit v3 does not contribute an implicit using for its own namespace, so every
// [Fact] in the project would otherwise need the directive repeated. One global
// using is the whole fix.
global using Xunit;
