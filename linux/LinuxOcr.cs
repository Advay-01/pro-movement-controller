using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace MovementPrototype {
    sealed class OcrResult {
        public readonly List<OcrLine> Lines=new List<OcrLine>();
    }
    sealed class OcrLine {
        public readonly List<OcrWord> Words=new List<OcrWord>();
        public string Text {
            get {
                var parts=new List<string>();
                foreach(var word in Words)parts.Add(word.Text);
                return String.Join(" ",parts);
            }
        }
    }
    sealed class OcrWord {
        public string Text;
        public Rectangle BoundingRect;
    }
    static class OcrEngine {
        public const int MaxImageDimension=2400;
    }
    static class LinuxOcr {
        const string Lib="libtesseract.so.5";
        const int TextLine=2,Word=3,SparseText=11;
        static readonly Queue<IntPtr> engines=new Queue<IntPtr>();
        static readonly System.Threading.SemaphoreSlim slots=
            new System.Threading.SemaphoreSlim(Math.Min(3,Math.Max(1,Environment.ProcessorCount)),3);

        [DllImport(Lib)] static extern IntPtr TessBaseAPICreate();
        [DllImport(Lib)] static extern void TessBaseAPIDelete(IntPtr api);
        [DllImport(Lib)] static extern int TessBaseAPIInit3(IntPtr api,IntPtr dataPath,string language);
        [DllImport(Lib)] static extern void TessBaseAPISetPageSegMode(IntPtr api,int mode);
        [DllImport(Lib)] static extern void TessBaseAPISetImage(IntPtr api,IntPtr pixels,int width,int height,int bpp,int stride);
        [DllImport(Lib)] static extern int TessBaseAPIRecognize(IntPtr api,IntPtr monitor);
        [DllImport(Lib)] static extern void TessBaseAPIClear(IntPtr api);
        [DllImport(Lib)] static extern IntPtr TessBaseAPIGetIterator(IntPtr api);
        [DllImport(Lib)] static extern IntPtr TessResultIteratorGetPageIterator(IntPtr iterator);
        [DllImport(Lib)] static extern int TessPageIteratorIsAtBeginningOf(IntPtr iterator,int level);
        [DllImport(Lib)] static extern int TessPageIteratorBoundingBox(IntPtr iterator,int level,out int l,out int t,out int r,out int b);
        [DllImport(Lib)] static extern IntPtr TessResultIteratorGetUTF8Text(IntPtr iterator,int level);
        [DllImport(Lib)] static extern int TessResultIteratorNext(IntPtr iterator,int level);
        [DllImport(Lib)] static extern void TessResultIteratorDelete(IntPtr iterator);
        [DllImport(Lib)] static extern void TessDeleteText(IntPtr text);
        [DllImport("libc.so.6")] static extern UIntPtr strlen(IntPtr value);

        static LinuxOcr() {
            AppDomain.CurrentDomain.ProcessExit+=delegate {
                lock(engines)while(engines.Count>0)TessBaseAPIDelete(engines.Dequeue());
            };
        }
        static IntPtr Acquire() {
            lock(engines)if(engines.Count>0)return engines.Dequeue();
            IntPtr api=TessBaseAPICreate();
            if(api==IntPtr.Zero)throw new InvalidOperationException("Could not create the Linux OCR engine.");
            if(TessBaseAPIInit3(api,IntPtr.Zero,"eng")!=0) {
                TessBaseAPIDelete(api);
                throw new InvalidOperationException("Tesseract could not load English OCR data. Install tesseract-ocr-eng.");
            }
            TessBaseAPISetPageSegMode(api,SparseText);
            return api;
        }
        static byte[] Grayscale(Bitmap bitmap) {
            var area=new Rectangle(Point.Empty,bitmap.Size);
            var bits=bitmap.LockBits(area,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try {
                var gray=new byte[bitmap.Width*bitmap.Height];
                var row=new byte[bitmap.Width*4];
                for(int y=0;y<bitmap.Height;y++) {
                    Marshal.Copy(IntPtr.Add(bits.Scan0,y*bits.Stride),row,0,row.Length);
                    for(int x=0;x<bitmap.Width;x++) {
                        int j=x*4;
                        gray[y*bitmap.Width+x]=(byte)((77*row[j+2]+150*row[j+1]+29*row[j])>>8);
                    }
                }
                return gray;
            } finally {bitmap.UnlockBits(bits);}
        }
        static string Decode(IntPtr p) {
            if(p==IntPtr.Zero)return "";
            try {
                var data=new byte[(int)strlen(p).ToUInt64()];
                Marshal.Copy(p,data,0,data.Length);
                return Encoding.UTF8.GetString(data).Trim();
            } finally {TessDeleteText(p);}
        }
        public static OcrResult Recognize(Bitmap image) {
            int longest=Math.Max(image.Width,image.Height);
            if(longest>OcrEngine.MaxImageDimension) {
                double ratio=(double)OcrEngine.MaxImageDimension/longest;
                using(var scaled=new Bitmap(image,Math.Max(1,(int)(image.Width*ratio)),Math.Max(1,(int)(image.Height*ratio)))) {
                    var result=Recognize(scaled);
                    double xScale=(double)image.Width/scaled.Width,yScale=(double)image.Height/scaled.Height;
                    foreach(var line in result.Lines)foreach(var word in line.Words) {
                        var rect=word.BoundingRect;
                        word.BoundingRect=Rectangle.FromLTRB((int)(rect.Left*xScale),(int)(rect.Top*yScale),
                            (int)Math.Ceiling(rect.Right*xScale),(int)Math.Ceiling(rect.Bottom*yScale));
                    }
                    return result;
                }
            }
            slots.Wait();
            IntPtr api=IntPtr.Zero,iterator=IntPtr.Zero;
            GCHandle pixels=default(GCHandle);
            try {
                api=Acquire();
                pixels=GCHandle.Alloc(Grayscale(image),GCHandleType.Pinned);
                TessBaseAPISetImage(api,pixels.AddrOfPinnedObject(),image.Width,image.Height,1,image.Width);
                if(TessBaseAPIRecognize(api,IntPtr.Zero)!=0)throw new InvalidOperationException("Tesseract could not read the captured screen.");
                var result=new OcrResult();
                iterator=TessBaseAPIGetIterator(api);
                if(iterator==IntPtr.Zero)return result;
                var page=TessResultIteratorGetPageIterator(iterator);
                OcrLine line=null;
                do {
                    if(line==null || TessPageIteratorIsAtBeginningOf(page,TextLine)!=0) {
                        line=new OcrLine();result.Lines.Add(line);
                    }
                    string word=Decode(TessResultIteratorGetUTF8Text(iterator,Word));
                    int l,t,r,b;
                    if(word.Length>0 && TessPageIteratorBoundingBox(page,Word,out l,out t,out r,out b)!=0)
                        line.Words.Add(new OcrWord {Text=word,BoundingRect=Rectangle.FromLTRB(l,t,r,b)});
                } while(TessResultIteratorNext(iterator,Word)!=0);
                result.Lines.RemoveAll(lineResult=>lineResult.Words.Count==0);
                return result;
            } finally {
                if(iterator!=IntPtr.Zero)TessResultIteratorDelete(iterator);
                if(api!=IntPtr.Zero) {
                    TessBaseAPIClear(api);
                    lock(engines)engines.Enqueue(api);
                }
                if(pixels.IsAllocated)pixels.Free();
                slots.Release();
            }
        }
    }
}
