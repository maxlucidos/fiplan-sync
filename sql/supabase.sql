-- Tabela de validacao do FIPLAN (FIPLAN Sync)
-- Rodar no SQL Editor do Supabase. Pode rodar de novo sem problema (tambem serve para atualizar).

-- Copia das notas de empenho da view ACWVW0111 (UO 19301, todos os exercicios)
CREATE TABLE IF NOT EXISTS public.fiplan_notas_empenho (
  numr_emp                  text PRIMARY KEY,         -- 193010001150002931
  numero                    text NOT NULL,            -- 19301.0001.15.000293-1 (formato do sistema)
  cd_exercicio              int NOT NULL,
  nome_credor               text,
  cpf_cnpj_credor           text,
  cd_elemento_despesa       int,
  ds_elemento_despesa       text,
  fonte                     text,
  codg_dotacao_orcamentaria text,
  empenhado                 numeric(15,2),
  estornado                 numeric(15,2),
  saldo                     numeric(15,2),
  pago                      numeric(15,2),
  ds_situacao_emp           text,
  sincronizado_em           timestamptz NOT NULL DEFAULT now()
);

-- Quem grava e o FIPLAN Sync, com a chave secret. Usuarios logados no sistema so leem
-- (a tela de validacao usa a view abaixo); visitantes sem login nao acessam.
ALTER TABLE public.fiplan_notas_empenho ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public.fiplan_notas_empenho FROM anon, authenticated;
GRANT SELECT ON public.fiplan_notas_empenho TO authenticated;
DROP POLICY IF EXISTS "leitura para usuarios logados" ON public.fiplan_notas_empenho;
CREATE POLICY "leitura para usuarios logados" ON public.fiplan_notas_empenho
  FOR SELECT TO authenticated USING (true);

-- Cada nota do sistema lado a lado com o FIPLAN, para a tela de validacao:
--   situacao  ok | diferente | nao encontrada
--   nota_id   id da linha em processo_notas_empenho (para o "validar" atualizar a nota certa)
--   alerta    mensagem pronta para exibir (vazia quando a nota esta ok)
-- security_invoker: quem consulta so ve as notas que ja poderia ver em processo_notas_empenho.
-- (Colunas novas entram sempre no fim, para o CREATE OR REPLACE funcionar sobre a versao anterior.)
CREATE OR REPLACE VIEW public.vw_fiplan_comparacao WITH (security_invoker = true) AS
SELECT n.processo_id,
       n.numero,
       f.cd_exercicio,
       f.nome_credor,
       n.valor       AS valor_sistema,
       f.empenhado   AS empenhado_fiplan,
       n.pagamentos  AS pago_sistema,
       f.pago        AS pago_fiplan,
       n.estornos    AS estornos_sistema,
       f.estornado   AS estornado_fiplan,
       f.saldo       AS saldo_fiplan,
       CASE
         WHEN f.numr_emp IS NULL THEN 'nao encontrada'
         WHEN d.valor OR d.pago OR d.estornos THEN 'diferente'
         ELSE 'ok'
       END AS situacao,
       f.sincronizado_em,
       n.id AS nota_id,
       CASE
         WHEN f.numr_emp IS NULL THEN 'Nota não encontrada no FIPLAN'
         ELSE nullif(concat_ws('; ',
           CASE WHEN d.pago THEN 'Pago desatualizado: sistema R$ ' || r.pago_sistema || ' · FIPLAN R$ ' || r.pago_fiplan END,
           CASE WHEN d.estornos THEN 'Estorno desatualizado: sistema R$ ' || r.estornos_sistema || ' · FIPLAN R$ ' || r.estornado_fiplan END,
           CASE WHEN d.valor THEN 'Valor empenhado diferente: sistema R$ ' || r.valor_sistema || ' · FIPLAN R$ ' || r.empenhado_fiplan END
         ), '')
       END AS alerta
FROM public.processo_notas_empenho n
LEFT JOIN public.fiplan_notas_empenho f
       ON f.numr_emp = regexp_replace(n.numero, '\D', '', 'g')
-- o que difere (tolerancia de meio centavo)
CROSS JOIN LATERAL (SELECT
       abs(coalesce(n.valor, 0)      - f.empenhado) > 0.005 AS valor,
       abs(coalesce(n.pagamentos, 0) - f.pago)      > 0.005 AS pago,
       abs(coalesce(n.estornos, 0)   - f.estornado) > 0.005 AS estornos) d
-- valores no formato brasileiro (1.234,56)
CROSS JOIN LATERAL (SELECT
       translate(to_char(coalesce(n.valor, 0),      'FM999,999,999,990.00'), ',.', '.,') AS valor_sistema,
       translate(to_char(f.empenhado,               'FM999,999,999,990.00'), ',.', '.,') AS empenhado_fiplan,
       translate(to_char(coalesce(n.pagamentos, 0), 'FM999,999,999,990.00'), ',.', '.,') AS pago_sistema,
       translate(to_char(f.pago,                    'FM999,999,999,990.00'), ',.', '.,') AS pago_fiplan,
       translate(to_char(coalesce(n.estornos, 0),   'FM999,999,999,990.00'), ',.', '.,') AS estornos_sistema,
       translate(to_char(f.estornado,               'FM999,999,999,990.00'), ',.', '.,') AS estornado_fiplan) r;

REVOKE ALL ON public.vw_fiplan_comparacao FROM anon, authenticated;
GRANT SELECT ON public.vw_fiplan_comparacao TO authenticated;

-- Avisa a API que existem objetos novos
NOTIFY pgrst, 'reload schema';
