namespace ConsoleApp4.Model
{
    public class Deputado
    {
        public string Nome { get; set; }
        public string UF { get; set; }
        public string Partido { get; set; }
        public string Email { get; set; }
        public string Telefone { get; set; }
        public string Estado { get; set; }
        public string Pagina { get; set; }

        public override string ToString()
        {
            return $"Nome: {Nome}\r\nUF: {UF}\r\nPartido: {Partido}\r\nE-mail: {Email}\r\nTelefone: {Telefone}\r\nEstado: {Estado}\r\nPagina: {Pagina}\r\n";
        }
    }
}
