﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tebaldi.MarketData.Models.State;

namespace Tebaldi.FeedImport.Business
{
    class BdiImportProcess : GenericProcess
    {
        // objeto responsavel por logar
        private static readonly log4net.ILog logger = log4net.LogManager.GetLogger("FeedImport.BdiImportProcess.cs");

        // arquivo de coniguracao
        private Tebaldi.BdiFeed.BdiConfig config;

        // Construtor
        public BdiImportProcess(Tebaldi.MarketData.Models.State.ProcessQueueState queue) : base(queue)
        { }

        public override void LoadConfig()
        {
            if (Queue.Process.Feed.KeyValues == null)
            { throw new Exceptions.KeyValuesNotLoaded("Key Values nao carregados"); }

            ValidadeKeyValue();

            string siteAddress = Queue.Process.Feed.GetValue("SiteAddress");
            string fileMask = Queue.Process.Feed.GetValue("FileMask");
            string saveDownloadAs = Queue.Process.Feed.GetValue("SaveDownloadAs");

            Tebaldi.BdiFeed.SourceFileInfo sourceFileInfo = new Tebaldi.BdiFeed.SourceFileInfo(
                siteAddress,
                ParseDateTimeMask(fileMask, Queue.DataReferencia),
            CreateFileInfo(ParseDateTimeMask(saveDownloadAs, Queue.DataReferencia)));


            string extractAs = Queue.Process.Feed.GetValue("ExtractAs");
            string searchInZip = Queue.Process.Feed.GetValue("SearchInZip");

            Tebaldi.BdiFeed.CompressedFileInfo compressInfo = new Tebaldi.BdiFeed.CompressedFileInfo();
            compressInfo.ExtracfileAs = CreateFileInfo(ParseDateTimeMask(extractAs, Queue.DataReferencia));
            compressInfo.File = sourceFileInfo.SaveFileAs;
            compressInfo.SearchForFile = searchInZip;

            config = new Tebaldi.BdiFeed.BdiConfig(Queue.DataReferencia, sourceFileInfo, compressInfo);
        }

        public Tebaldi.BdiFeed.DataClass.State.BdiFileState ImportaBdi()
        {
            return ImportaBdi(true);
        }

        public Tebaldi.BdiFeed.DataClass.State.BdiFileState ImportaBdi(bool dispose)
        {
            Tebaldi.BdiFeed.BdiHandler handler = new Tebaldi.BdiFeed.BdiHandler(config);

            Tebaldi.BdiFeed.DataClass.State.BdiFileState bdiFile = new Tebaldi.BdiFeed.DataClass.State.BdiFileState();

            try
            {
                handler.DownloadBdiFile();
                handler.DecompressBdiZip();
                bdiFile = handler.ReadBdiFile();
                handler.CheckDate(bdiFile);
            }
            catch (System.Net.WebException ex)
            {
                System.Net.HttpWebResponse errorResponse = ex.Response as System.Net.HttpWebResponse;
                if (errorResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    logger.Info("QueueId: " + this.Queue.QueueId.ToString() + " - Erro no download do arquivo BDI Status Code: " + ex.Message);
                    throw new Exceptions.DownloadError404Exception();
                }
                else
                { throw; }
            }
            finally
            {
                if (dispose)
                { handler.Dispose(); }
            }

            return bdiFile;
        }

        private void BdiToDataTable(Tebaldi.BdiFeed.DataClass.State.BdiFileState bdiFile)
        {
            try
            {
                FillCotacoes(bdiFile);
                FillIndices(bdiFile);
            }
            catch (Exception ex)
            {
                logger.Error("Erro no processamento do arquivo bdi", ex);
                throw;
            }
        }

        private void FillCotacoes(Tebaldi.BdiFeed.DataClass.State.BdiFileState bdiFile)
        {
            foreach (Tebaldi.BdiFeed.DataClass.State.BdiFile.CotacaoState cotacao in bdiFile.Cotacoes)
            {
                DataRow row = Data.NewRow();

                row["EXT_ID"] = cotacao.Ticker;
                row["ProcessId"] = Queue.Process.Id;
                row["AtivoId"] = 0;
                row["DataPregao"] = bdiFile.Header.DataDoPregao;
                row["NomeResumido"] = cotacao.NomeResumido;
                row["EspecPapel"] = cotacao.EspecificacaoPapel;
                row["Ticker"] = cotacao.Ticker;
                row["TipoMercado"] = cotacao.TipoDeMercado;
                row["PrecoAbertura"] = cotacao.PrecoAbertura;
                row["PrecoMaximo"] = cotacao.PrecoMaximo;
                row["PrecoMedio"] = cotacao.PrecoMedio;
                row["PrecoMinimo"] = cotacao.PrecoMinimo;
                row["PrecoFechamento"] = cotacao.PrecoFechamento;
                row["Quantidade"] = cotacao.Quantidade;
                row["TotalNegocios"] = cotacao.TotalNegocios;
                row["Volume"] = cotacao.Volume;
                row["ISIN"] = cotacao.CodISIN;

                if (cotacao.SinalOscilacao == "+")
                { row["Variacao"] = cotacao.Oscilacao / 100; }
                else
                { row["Variacao"] = -cotacao.Oscilacao / 100; }

                Data.Rows.Add(row);
            }
        }

        private void FillIndices(Tebaldi.BdiFeed.DataClass.State.BdiFileState bdiFile)
        {
            foreach (Tebaldi.BdiFeed.DataClass.State.BdiFile.IndiceState indice in bdiFile.Indices)
            {
                DataRow row = Data.NewRow();

                row["EXT_ID"] = indice.NomeIndice;
                row["ProcessId"] = Queue.Process.Id;
                row["AtivoId"] = 0;
                row["DataPregao"] = bdiFile.Header.DataDoPregao;
                row["NomeResumido"] = indice.NomeIndice;
                row["EspecPapel"] = "INDICE";
                row["Ticker"] = indice.NomeIndice;
                row["TipoMercado"] = 99;
                row["PrecoAbertura"] = indice.IndiceAbertura;
                row["PrecoMaximo"] = indice.IndiceMaximo;
                row["PrecoMedio"] = indice.IndiceMedia;
                row["PrecoMinimo"] = indice.IndiceMinimo;
                row["PrecoFechamento"] = indice.IndiceFechamento;
                row["Quantidade"] = indice.QtdTitulosNegociadosIndice;
                row["TotalNegocios"] = indice.NegociosComAcoesDoIndice;
                row["Volume"] = indice.VolumeNegociosDoIndice;
                row["ISIN"] = "";

                if (indice.OntemSinalEvolucao == "+")
                { row["Variacao"] = indice.OntemEvolucaoPercentual; }
                else
                { row["Variacao"] = -indice.OntemEvolucaoPercentual; }

                Data.Rows.Add(row);
            }
        }

        public override void ExecuteFeed()
        {
            BdiToDataTable(ImportaBdi(false));
        }

        private FileInfo CreateFileInfo(string file)
        {
            return new FileInfo(Path.Combine(WorkingDir.FullName, file));
        }

        private void ValidadeKeyValue()
        {
            string strErrorMessage = "";
            if (Queue.Process.Feed.KeyValues.FindIndex(kv => kv.Key == "SiteAddress") < 0)
            { strErrorMessage = strErrorMessage + "Key \'SiteAddress\' nao encontrada\n"; }

            if (Queue.Process.Feed.KeyValues.FindIndex(kv => kv.Key == "FileMask") < 0)
            { strErrorMessage = strErrorMessage + "Key \'FileMask\' nao encontrada\n"; }

            if (Queue.Process.Feed.KeyValues.FindIndex(kv => kv.Key == "SaveDownloadAs") < 0)
            { strErrorMessage = strErrorMessage + "Key \'SaveDownloadAs\' nao encontrada\n"; }

            if (Queue.Process.Feed.KeyValues.FindIndex(kv => kv.Key == "ExtractAs") < 0)
            { strErrorMessage = strErrorMessage + "Key \'ExtractAs\' nao encontrada\n"; }

            if (Queue.Process.Feed.KeyValues.FindIndex(kv => kv.Key == "SearchInZip") < 0)
            { strErrorMessage = strErrorMessage + "Key \'SearchInZip\' nao encontrada\n"; }

            if (!String.IsNullOrEmpty(strErrorMessage))
            {
                logger.Error(strErrorMessage);
                throw new Exception(strErrorMessage);
            }
        }
    }
}
