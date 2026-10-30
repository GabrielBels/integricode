return args switch
{
    ["review", ..] => 0,
    ["comment", ..] => 0,
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("Usage: integricode <review|comment> [options]");
    return 2;
}
