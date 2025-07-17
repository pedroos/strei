public static class Utils {
    // Executes a function, handling a possible exception. Aims to internalize 
    // the try/catch clause and return a single consistent type.
    
    public static T Try<T, TEx>(Func<T> f, Func<TEx, T> fex) 
        where TEx : Exception 
    {
        try {
            return f();
        }
        catch (TEx ex) {
            return fex(ex);
        }
    }
}