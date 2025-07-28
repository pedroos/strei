public static class Utils {
    // Executes a function, handling a possible exception. Aims to internalize 
    // the try/catch clause and enforce a common return type.
    
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