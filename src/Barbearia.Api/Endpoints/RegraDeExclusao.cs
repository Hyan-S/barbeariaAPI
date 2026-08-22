namespace Barbearia.Api.Endpoints;

// Regra unica de exclusao de cadastro, valendo para servico, funcionario, produto e
// cliente. Sao duas travas, e cada uma existe por um motivo diferente.
//
// A primeira e "so sai o que esta desligado". Excluir e definitivo e nao ha lixeira,
// entao desativar antes e o passo em que ainda da para mudar de ideia: quem clicou
// errado ve o cadastro sumir das telas de marcar e volta atras sem perder nada. Sem
// essa trava, um clique numa lista cheia apaga um servico que a agenda de amanha usa.
//
// A segunda e "nao sai o que tem historico apontando para ele". No banco os
// agendamentos apontam para servico, barbeiro e cliente com Restrict, e um pedido
// vendido aponta para o produto em Cascade: apagar o cadastro quebraria o registro de
// um atendimento que aconteceu, ou o levaria embora junto — e com ele o caixa daquele
// dia. Quando isso acontece o cadastro fica inativo para sempre, e e o certo: ele nao
// aparece mais para marcar, e o passado continua legivel.
//
// A recusa e 409 e nao 400 de proposito: o pedido esta bem formado, quem recusa e o
// estado em que o cadastro esta. O front trata os dois igual (le o campo "erro"), mas
// quem for ler o log depois enxerga a diferenca entre "mandou errado" e "nao pode
// agora".
internal static class RegraDeExclusao
{
    public static IResult Recusa(string erro) => Results.Conflict(new { erro });

    // "1 agendamento" em vez de "1 agendamentos". A mensagem e o unico lugar onde a
    // pessoa descobre por que o botao nao funcionou; vale escrever direito.
    public static string Contagem(int quantos, string singular, string plural) =>
        quantos == 1 ? $"1 {singular}" : $"{quantos} {plural}";
}
