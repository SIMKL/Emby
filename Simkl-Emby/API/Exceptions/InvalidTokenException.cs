using System;

namespace Simkl.Api.Exceptions {
    public class InvalidTokenException : Exception {
        public InvalidTokenException() {}
        public InvalidTokenException(string msg) : base (msg) {}
        public InvalidTokenException(string msg, Exception inner) : base (msg, inner) {}
    }
}