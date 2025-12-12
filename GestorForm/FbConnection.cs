
namespace LsiGestor
{
    internal class FbConnection
    {
        private string connectionString;

        public FbConnection(string connectionString)
        {
            this.connectionString = connectionString;
        }

        internal void Open()
        {
            throw new NotImplementedException();
        }
    }
}