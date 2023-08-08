using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;
using ConsoleApp4.Model;

namespace ConsoleApp4
{
    class Program
    {
        static readonly HttpClient clienteHttp = new HttpClient();

        const string caminhoDoArquivoDeputadosExtratorBase = @"C:\deputados-base.html";
        const string caminhoDoArquivoDeputadosExtratorTratadoParaXml = @"C:\deputados.xml";

        const string caminhoDoArquivoHtmlDeputadoBase = @"C:\deputado-base.html";
        const string caminhoDoArquivoDeputadoExtratorTratadoParaXml = @"C:\deputado.xml";

        private const string templateUrlPaginaListagemDeputados = "https://www.camara.leg.br/deputados/quem-sao/resultado?search=&partido=&uf={0}&legislatura=&sexo=&pagina={1}";

        const string mensagemParadaBuscaDeputados = "Nenhuma ocorrência encontrada para sua pesquisa.";

        static void RemoveArquivosAuxiliares()
        {
            File.Delete(caminhoDoArquivoDeputadosExtratorBase);
            File.Delete(caminhoDoArquivoDeputadosExtratorTratadoParaXml);

            File.Delete(caminhoDoArquivoHtmlDeputadoBase);
            File.Delete(caminhoDoArquivoDeputadoExtratorTratadoParaXml);
        }

        static async Task<string> CarregaPagina(string url)
        {
            using (var conteudoPagina = await clienteHttp.GetAsync(url))
            {
                conteudoPagina.EnsureSuccessStatusCode();
                return await conteudoPagina.Content.ReadAsStringAsync();
            }
        }

        static async Task EscreveArquivoHtml(string caminhoArquivo, string conteudoPagina)
        {
            using (var escritor = new StreamWriter(caminhoArquivo))
            {
                await escritor.WriteAsync(conteudoPagina);
                await escritor.FlushAsync();
            }
        }

        static async Task ExtraiPorcaoHtmlComDadosParaArquivo(string caminhoArquivoBase, string caminhoArquivoPorcao, string identificadorInicioPorcao)
        {
            using (var leitor = new StreamReader(caminhoArquivoBase))
            {
                bool elementoHtmlEncontrado = false;
                using (var escritor = new StreamWriter(caminhoArquivoPorcao))
                {
                    while (!leitor.EndOfStream)
                    {
                        var linha = leitor.ReadLine();
                        if (linha.Trim().Equals(identificadorInicioPorcao))
                            elementoHtmlEncontrado = true;

                        if (elementoHtmlEncontrado)
                        {
                            // Ma formacao do HTML descartada para conseguir converter em XML
                            if (linha.Trim().StartsWith("<img"))
                                continue;

                            await escritor.WriteLineAsync(linha);
                            await escritor.FlushAsync();

                            if (linha.Trim().Equals("</ul>"))
                            {
                                break;
                            }
                        }
                    }
                }
            }
        }

        static async Task<Tuple<string, string>> ExtraiEmailETelefoneDeputado(string paginaDeputado)
        {
            string emailDeputado, telefoneDeputado;

            string conteudoPagina = await CarregaPagina(paginaDeputado);
            await EscreveArquivoHtml(caminhoDoArquivoHtmlDeputadoBase, conteudoPagina);
            await ExtraiPorcaoHtmlComDadosParaArquivo(caminhoDoArquivoHtmlDeputadoBase,
                caminhoDoArquivoDeputadoExtratorTratadoParaXml, "<ul class=\"informacoes-deputado\" aria-labelledby=\"nomedeputado\">");

            XDocument paginaDeputadoHtmlComoXML;
            try
            {
                paginaDeputadoHtmlComoXML = XDocument.Parse(File.ReadAllText(caminhoDoArquivoDeputadoExtratorTratadoParaXml));
            }
            catch 
            {
                // Alguns deputados nao tem informacao de e-mail e telefone - situacao 1
                Console.WriteLine($"Falha ao validar a pagina {paginaDeputado} como XML para extrair e-mail e telefone");
                return new Tuple<string, string>(string.Empty, string.Empty);
            }
            
            var listaElementosHtmlEmailDeputado = paginaDeputadoHtmlComoXML.XPathSelectElement("//a[@class='email']");
            var listaElementosHtmlTelefoneDeputado = paginaDeputadoHtmlComoXML.XPathSelectElement("//span[contains(text(),'Telefone')]");

            try
            {
                emailDeputado = listaElementosHtmlEmailDeputado.Value;
                telefoneDeputado = listaElementosHtmlTelefoneDeputado.Parent.Value.ToLower().Replace("telefone: ", "");

                return new Tuple<string, string>(emailDeputado, telefoneDeputado);
            }
            catch (Exception e)
            {
                // Alguns deputados nao tem informacao de e-mail e telefone - situacao 2
                Console.WriteLine($"O(a) deputado(a) da pagina {paginaDeputado} nao tem email ou telefone cadastrado.");
                return new Tuple<string, string>(string.Empty, string.Empty);
            }
        }

        static async Task<Deputado> CarregaDadosDeputado(XElement elementosHtmlDeputado)
        {
            var elementoHtmlLinkNomeDeputado = elementosHtmlDeputado.Descendants("a").First();

            // Obtem nome do deputado
            var nomeUfEPartidoDeputado = elementoHtmlLinkNomeDeputado.Value;
            var nomeDoDeputado =
                nomeUfEPartidoDeputado.Substring(0, nomeUfEPartidoDeputado.IndexOf("(") - 1);

            // Obtem partido e UF do deputado
            var tempPartidoEUf = nomeUfEPartidoDeputado.Substring(nomeUfEPartidoDeputado.IndexOf("("));
            tempPartidoEUf = tempPartidoEUf.Replace("(", "").Replace(")", "").Trim();
            var tempPartidoEUfVetor = tempPartidoEUf.Split(new[] { '-' });

            var partidoDoDeputado = tempPartidoEUfVetor[0];
            var ufDoDeputado = tempPartidoEUfVetor[1];

            // Obtem pagina do deputado
            var paginaDeputado = elementoHtmlLinkNomeDeputado.Attribute("href").Value;

            // Obtem estado do deputado
            var estadoDeputado = elementosHtmlDeputado.Descendants("span").First().Value.Trim();

            // Extrai contato de cada deputado em pagina especifica de deputado
            var emailETelefone = await ExtraiEmailETelefoneDeputado(paginaDeputado);

            return new Deputado
            {
                Nome = nomeDoDeputado,
                UF = ufDoDeputado,
                Partido = partidoDoDeputado,
                Email = emailETelefone.Item1,
                Telefone = emailETelefone.Item2,
                Estado = estadoDeputado,
                Pagina = paginaDeputado,
            };
        }

        static async Task<List<Deputado>> CarregaDadosDeputados(IEnumerable<XElement> listaDeElementosHtmlDosDeputados)
        {
            var listaDeDeputados = new List<Deputado>();
            foreach (var elementoHtmlDoDeputado in listaDeElementosHtmlDosDeputados)
            {
                // Popula lista de deputados
                listaDeDeputados.Add(await CarregaDadosDeputado(elementoHtmlDoDeputado));
            }
            return listaDeDeputados;
        }

        static string ObtemHostSmtpRemetente(string emailUsuario)
        {
            string dnsServidorSmpt = emailUsuario.Substring(emailUsuario.IndexOf("@") + 1);

            string servidorSmtp = string.Empty;
            switch (dnsServidorSmpt.ToLower())
            {
                case "gmail.com":
                    servidorSmtp = "smtp.gmail.com";
                    break;
                case "hotmail.com":
                case "outlook.com":
                    servidorSmtp = "smtp.office365.com ";
                    break;
            }

            return servidorSmtp;
        }

        static async Task EnviaEmail(string nomeUsuario, string emailUsuario, string senhaUsuario, string assunto, string mensagem, Deputado deputado)
        {
            var emailRemetente = new MailAddress(emailUsuario, nomeUsuario);
            
#if DEBUG
            var emailDestinatario = new MailAddress("diegosousa88@gmail.com", "Diego");
#else
            var emailDestinatario = new MailAddress(deputado.Email, deputado.Nome);
#endif

            // Define o servidor SMTP de acordo com o email do Usuario
            var host = ObtemHostSmtpRemetente(emailRemetente.Address);

            var clienteSmtp = new SmtpClient
            {
                Host = "smtp.gmail.com",
                Port = 587,
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(emailRemetente.Address, senhaUsuario)
            };
            using (var message = new MailMessage(emailRemetente, emailDestinatario)
                   {
                       Subject = assunto,
                       Body = mensagem
            })
            {
                Console.WriteLine($"Enviando e-mail de {emailRemetente.Address} para {emailDestinatario.Address}");
                // Falhando devido a autenticacao de dois fatores. Descobrir como enviar e-mail nessa situacao.
                await clienteSmtp.SendMailAsync(message);
            }
        }

        static string ObtemVersaoAplicativo()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var metadataDoArquivoAssembly = FileVersionInfo.GetVersionInfo(assembly.Location);
            return metadataDoArquivoAssembly.FileVersion;
        }

        static async Task Main(string[] args)
        {
            Console.WriteLine($"Comunicador de deputados v{ObtemVersaoAplicativo()}");
            Console.WriteLine();

            Console.Write("Informe seu nome: ");
            var nomeUsuario = Console.ReadLine();

            Console.Write("Informe seu e-mail: ");
            var emailUsuario = Console.ReadLine();

            Console.Write("Informe a senha do seu e-mail: ");
            var senhaUsuario = Console.ReadLine();

            Console.Write("Informe o assunto do seu e-mail: ");
            var assuntoEmail = Console.ReadLine();

            Console.Write("Informe a mensagem do seu e-mail: ");
            var mensagemEmail = Console.ReadLine();

            Console.Write("Informe seu UF: ");
            var uf = Console.ReadLine();

            RemoveArquivosAuxiliares();

            var listaDeputados = new List<Deputado>();
            int numeroPagina = 0;
            while (true)
            {
                numeroPagina++;
                var url = string.Format(templateUrlPaginaListagemDeputados, uf, numeroPagina);
                var conteudoPagina =  await CarregaPagina(url);

                if (conteudoPagina.Contains(mensagemParadaBuscaDeputados))
                    break;

                await EscreveArquivoHtml(caminhoDoArquivoDeputadosExtratorBase, conteudoPagina);

                await ExtraiPorcaoHtmlComDadosParaArquivo(caminhoDoArquivoDeputadosExtratorBase, caminhoDoArquivoDeputadosExtratorTratadoParaXml,
                    "<ul class=\"lista-resultados\">");

                var paginaHtmlComoXML = XDocument.Parse(File.ReadAllText(caminhoDoArquivoDeputadosExtratorTratadoParaXml));
                var listaDeElementosHtmlDosDeputados = paginaHtmlComoXML.XPathSelectElements("/ul/li[@class='lista-resultados__item']");

                listaDeputados.AddRange(await CarregaDadosDeputados(listaDeElementosHtmlDosDeputados));

#if DEBUG
                if (numeroPagina == 1)
                    break;
#endif
            }

            if (listaDeputados.Count == 0)
            {
                Console.WriteLine("Nenhum deputado encontrado pelo gerenciador de deputados");
                return;
            }

            // Envia e-mail somente para deputados em exercicio
            foreach (var deputadoEmExercicio in listaDeputados.Where(_ => _.Estado.ToLower().Equals("em exercício")))
            {
                Console.WriteLine(deputadoEmExercicio);
                await EnviaEmail(nomeUsuario, emailUsuario, senhaUsuario, assuntoEmail, mensagemEmail, deputadoEmExercicio);
            }
        }
    }
}
