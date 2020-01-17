//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

namespace Ice
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Runtime.Serialization;
    using System.Runtime.Serialization.Formatters.Binary;
    using Protocol = IceInternal.Protocol;

    /// <summary>
    /// Throws a UserException corresponding to the given Slice type Id, such as "::Module::MyException".
    /// If the implementation does not throw an exception, the Ice run time will fall back
    /// to using its default behavior for instantiating the user exception.
    /// </summary>
    /// <param name="id">A Slice type Id corresponding to a Slice user exception.</param>
    public delegate void UserExceptionFactory(string id);

    /// <summary>
    /// Interface for input streams used to extract Slice types from a sequence of bytes.
    /// </summary>
    public class InputStream
    {
        /// <summary>
        /// This constructor uses the communicator's default encoding version.
        /// </summary>
        /// <param name="communicator">The communicator to use when initializing the stream.</param>
        public InputStream(Communicator communicator)
        {
            Initialize(communicator);
            _buf = new IceInternal.Buffer();
        }

        /// <summary>
        /// This constructor uses the communicator's default encoding version.
        /// </summary>
        /// <param name="communicator">The communicator to use when initializing the stream.</param>
        /// <param name="data">The byte array containing encoded Slice types.</param>
        public InputStream(Communicator communicator, byte[] data)
        {
            Initialize(communicator);
            _buf = new IceInternal.Buffer(data);
        }

        public InputStream(Communicator communicator, IceInternal.ByteBuffer buf)
        {
            Initialize(communicator);
            _buf = new IceInternal.Buffer(buf);
        }

        public InputStream(Communicator communicator, IceInternal.Buffer buf) :
            this(communicator, buf, false)
        {
        }

        public InputStream(Communicator communicator, IceInternal.Buffer buf, bool adopt)
        {
            Initialize(communicator);
            _buf = new IceInternal.Buffer(buf, adopt);
        }

        /// <summary>
        /// This constructor uses the given encoding version.
        /// </summary>
        /// <param name="communicator">The communicator to use when initializing the stream.</param>
        /// <param name="encoding">The desired encoding version.</param>
        public InputStream(Communicator communicator, EncodingVersion encoding)
        {
            Initialize(communicator, encoding);
            _buf = new IceInternal.Buffer();
        }

        /// <summary>
        /// This constructor uses the given encoding version.
        /// </summary>
        /// <param name="communicator">The communicator to use when initializing the stream.</param>
        /// <param name="encoding">The desired encoding version.</param>
        /// <param name="data">The byte array containing encoded Slice types.</param>
        public InputStream(Communicator communicator, EncodingVersion encoding, byte[] data)
        {
            Initialize(communicator, encoding);
            _buf = new IceInternal.Buffer(data);
        }

        public InputStream(Communicator communicator, EncodingVersion encoding, IceInternal.ByteBuffer buf)
        {
            Initialize(communicator, encoding);
            _buf = new IceInternal.Buffer(buf);
        }

        public InputStream(Communicator communicator, EncodingVersion encoding, IceInternal.Buffer buf) :
            this(communicator, encoding, buf, false)
        {
        }

        public InputStream(Communicator communicator, EncodingVersion encoding, IceInternal.Buffer buf, bool adopt)
        {
            Initialize(communicator, encoding);
            _buf = new IceInternal.Buffer(buf, adopt);
        }

        /// <summary>
        /// Initializes the stream to use the communicator's default encoding version.
        /// </summary>
        /// <param name="communicator">The communicator to use when initializing the stream.</param>
        public void Initialize(Communicator communicator)
        {
            Debug.Assert(communicator != null);
            Initialize(communicator, communicator.defaultsAndOverrides().defaultEncoding);
        }

        private void Initialize(Ice.Communicator communicator, EncodingVersion encoding)
        {
            _encoding = encoding;

            _communicator = communicator;
            _traceSlicing = _communicator.traceLevels().slicing > 0;
            _classGraphDepthMax = _communicator.classGraphDepthMax();
            _logger = _communicator.Logger;
            _classResolver = _communicator.resolveClass;
            _encapsStack = null;
            _encapsCache = null;
            _closure = null;
            _sliceClasses = true;
        }

        /// <summary>
        /// Resets this stream. This method allows the stream to be reused, to avoid creating
        /// unnecessary garbage.
        /// </summary>
        public void Reset()
        {
            _buf.reset();
            Clear();
        }

        /// <summary>
        /// Releases any data retained by encapsulations. Internally calls clear().
        /// </summary>
        public void Clear()
        {
            if (_encapsStack != null)
            {
                Debug.Assert(_encapsStack.next == null);
                _encapsStack.next = _encapsCache;
                _encapsCache = _encapsStack;
                _encapsStack = null;
                _encapsCache.reset();
            }

            _sliceClasses = true;
        }

        /// <summary>
        /// Sets the logger to use when logging trace messages. If the stream
        /// was initialized with a communicator, the communicator's logger will
        /// be used by default.
        /// </summary>
        /// <param name="logger">The logger to use for logging trace messages.</param>
        public void SetLogger(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Sets the compact ID resolver to use when unmarshaling value and exception
        /// instances. If the stream was initialized with a communicator, the communicator's
        /// resolver will be used by default.
        /// </summary>
        /// <param name="r">The compact ID resolver.</param>
        public void SetCompactIdResolver(Func<int, string> r)
        {
            _compactIdResolver = r;
        }

        /// <summary>
        /// Sets the class resolver, which the stream will use when attempting to unmarshal
        /// a value or exception. If the stream was initialized with a communicator, the communicator's
        /// resolver will be used by default.
        /// </summary>
        /// <param name="r">The class resolver.</param>
        public void SetClassResolver(Func<string, Type> r)
        {
            _classResolver = r;
        }

        /// <summary>
        /// Determines the behavior of the stream when extracting instances of Slice classes.
        /// An instance is "sliced" when a factory cannot be found for a Slice type ID.
        /// The stream's default behavior is to slice instances.
        /// </summary>
        /// <param name="b">If true (the default), slicing is enabled; if false,
        /// slicing is disabled. If slicing is disabled and the stream encounters a Slice type ID
        /// during decoding for which no class factory is installed, it raises NoClassFactoryException.
        /// </param>
        public void SetSliceClasses(bool b)
        {
            _sliceClasses = b;
        }

        /// <summary>
        /// Determines whether the stream logs messages about slicing instances of Slice values.
        /// </summary>
        /// <param name="b">True to enable logging, false to disable logging.</param>
        public void SetTraceSlicing(bool b)
        {
            _traceSlicing = b;
        }

        /// <summary>
        /// Set the maximum depth allowed for graph of Slice class instances.
        /// </summary>
        /// <param name="classGraphDepthMax">The maximum depth.</param>
        public void SetClassGraphDepthMax(int classGraphDepthMax)
        {
            if (classGraphDepthMax < 1)
            {
                _classGraphDepthMax = 0x7fffffff;
            }
            else
            {
                _classGraphDepthMax = classGraphDepthMax;
            }
        }

        public Communicator? Communicator()
        {
            return _communicator;
        }

        /// <summary>
        /// Swaps the contents of one stream with another.
        /// </summary>
        /// <param name="other">The other stream.</param>
        public void Swap(InputStream other)
        {
            Debug.Assert(_communicator == other._communicator);

            IceInternal.Buffer tmpBuf = other._buf;
            other._buf = _buf;
            _buf = tmpBuf;

            EncodingVersion tmpEncoding = other._encoding;
            other._encoding = _encoding;
            _encoding = tmpEncoding;

            bool tmpTraceSlicing = other._traceSlicing;
            other._traceSlicing = _traceSlicing;
            _traceSlicing = tmpTraceSlicing;

            object? tmpClosure = other._closure;
            other._closure = _closure;
            _closure = tmpClosure;

            bool tmpSliceClasses = other._sliceClasses;
            other._sliceClasses = _sliceClasses;
            _sliceClasses = tmpSliceClasses;

            int tmpClassGraphDepthMax = other._classGraphDepthMax;
            other._classGraphDepthMax = _classGraphDepthMax;
            _classGraphDepthMax = tmpClassGraphDepthMax;

            //
            // Swap is never called for InputStreams that have encapsulations being read. However,
            // encapsulations might still be set in case un-marshalling failed. We just
            // reset the encapsulations if there are still some set.
            //
            ResetEncapsulation();
            other.ResetEncapsulation();

            int tmpMinTotalSeqSize = other._minTotalSeqSize;
            other._minTotalSeqSize = _minTotalSeqSize;
            _minTotalSeqSize = tmpMinTotalSeqSize;

            ILogger tmpLogger = other._logger;
            other._logger = _logger;
            _logger = tmpLogger;

            Func<int, string> tmpCompactIdResolver = other._compactIdResolver;
            other._compactIdResolver = _compactIdResolver;
            _compactIdResolver = tmpCompactIdResolver;

            Func<string, Type?> tmpClassResolver = other._classResolver;
            other._classResolver = _classResolver;
            _classResolver = tmpClassResolver;
        }

        private void ResetEncapsulation()
        {
            _encapsStack = null;
        }

        /// <summary>
        /// Resizes the stream to a new size.
        /// </summary>
        /// <param name="sz">The new size.</param>
        public void Resize(int sz)
        {
            _buf.resize(sz, true);
            _buf.b.position(sz);
        }

        public IceInternal.Buffer GetBuffer()
        {
            return _buf;
        }

        /// <summary>
        /// Marks the start of a class instance.
        /// </summary>
        public void StartClass()
        {
            Debug.Assert(_encapsStack != null && _encapsStack.decoder != null);
            _encapsStack.decoder.startInstance(SliceType.ClassSlice);
        }

        /// <summary>
        /// Marks the end of a class instance.
        /// </summary>
        /// <param name="preserve">True if unknown slices should be preserved, false otherwise.</param>
        /// <returns>A SlicedData object containing the preserved slices for unknown types.</returns>
        public SlicedData EndClass(bool preserve)
        {
            Debug.Assert(_encapsStack != null && _encapsStack.decoder != null);
            return _encapsStack.decoder.endInstance(preserve);
        }

        /// <summary>
        /// Marks the start of a user exception.
        /// </summary>
        public void StartException()
        {
            Debug.Assert(_encapsStack != null && _encapsStack.decoder != null);
            _encapsStack.decoder.startInstance(SliceType.ExceptionSlice);
        }

        /// <summary>
        /// Marks the end of a user exception.
        /// </summary>
        /// <param name="preserve">True if unknown slices should be preserved, false otherwise.</param>
        /// <returns>A SlicedData object containing the preserved slices for unknown types.</returns>
        public SlicedData EndException(bool preserve)
        {
            Debug.Assert(_encapsStack != null && _encapsStack.decoder != null);
            return _encapsStack.decoder.endInstance(preserve);
        }

        /// <summary>
        /// Reads the start of an encapsulation.
        /// </summary>
        /// <returns>The encapsulation encoding version.</returns>
        public EncodingVersion StartEncapsulation()
        {
            Encaps curr = _encapsCache;
            if (curr != null)
            {
                curr.reset();
                _encapsCache = _encapsCache.next;
            }
            else
            {
                curr = new Encaps();
            }
            curr.next = _encapsStack;
            _encapsStack = curr;

            _encapsStack.start = _buf.b.position();

            //
            // I don't use readSize() for encapsulations, because when creating an encapsulation,
            // I must know in advance how many bytes the size information will require in the data
            // stream. If I use an Int, it is always 4 bytes. For readSize(), it could be 1 or 5 bytes.
            //
            int sz = ReadInt();
            if (sz < 6)
            {
                throw new UnmarshalOutOfBoundsException();
            }
            if (sz - 4 > _buf.b.remaining())
            {
                throw new UnmarshalOutOfBoundsException();
            }
            _encapsStack.sz = sz;

            byte major = ReadByte();
            byte minor = ReadByte();
            EncodingVersion encoding = new EncodingVersion(major, minor);
            Protocol.checkSupportedEncoding(encoding); // Make sure the encoding is supported.
            _encapsStack.setEncoding(encoding);

            return encoding;
        }

        /// <summary>
        /// Ends the previous encapsulation.
        /// </summary>
        public void EndEncapsulation()
        {
            Debug.Assert(_encapsStack != null);

            if (!_encapsStack.encoding_1_0)
            {
                skipOptionals();
                if (_buf.b.position() != _encapsStack.start + _encapsStack.sz)
                {
                    throw new EncapsulationException();
                }
            }
            else if (_buf.b.position() != _encapsStack.start + _encapsStack.sz)
            {
                if (_buf.b.position() + 1 != _encapsStack.start + _encapsStack.sz)
                {
                    throw new EncapsulationException();
                }

                //
                // Ice version < 3.3 had a bug where user exceptions with
                // class members could be encoded with a trailing byte
                // when dispatched with AMD. So we tolerate an extra byte
                // in the encapsulation.
                //
                try
                {
                    _buf.b.get();
                }
                catch (InvalidOperationException ex)
                {
                    throw new UnmarshalOutOfBoundsException(ex);
                }
            }

            Encaps curr = _encapsStack;
            _encapsStack = curr.next;
            curr.next = _encapsCache;
            _encapsCache = curr;
            _encapsCache.reset();
        }

        /// <summary>
        /// Skips an empty encapsulation.
        /// </summary>
        /// <returns>The encapsulation's encoding version.</returns>
        public EncodingVersion SkipEmptyEncapsulation()
        {
            int sz = ReadInt();
            if (sz < 6)
            {
                throw new EncapsulationException();
            }
            if (sz - 4 > _buf.b.remaining())
            {
                throw new UnmarshalOutOfBoundsException();
            }
            byte major = ReadByte();
            byte minor = ReadByte();
            var encoding = new EncodingVersion(major, minor);
            Protocol.checkSupportedEncoding(encoding); // Make sure the encoding is supported.

            if (encoding.Equals(Util.Encoding_1_0))
            {
                if (sz != 6)
                {
                    throw new EncapsulationException();
                }
            }
            else
            {
                // Skip the optional content of the encapsulation if we are expecting an
                // empty encapsulation.
                _buf.b.position(_buf.b.position() + sz - 6);
            }
            return encoding;
        }

        /// <summary>
        /// Returns a blob of bytes representing an encapsulation. The encapsulation's encoding version
        /// is returned in the argument.
        /// </summary>
        /// <param name="encoding">The encapsulation's encoding version.</param>
        /// <returns>The encoded encapsulation.</returns>
        public byte[] ReadEncapsulation(out EncodingVersion encoding)
        {
            int sz = ReadInt();
            if (sz < 6)
            {
                throw new UnmarshalOutOfBoundsException();
            }

            if (sz - 4 > _buf.b.remaining())
            {
                throw new UnmarshalOutOfBoundsException();
            }

            byte major = ReadByte();
            byte minor = ReadByte();
            encoding = new EncodingVersion(major, minor);
            _buf.b.position(_buf.b.position() - 6);

            byte[] v = new byte[sz];
            try
            {
                _buf.b.get(v);
                return v;
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Determines the current encoding version.
        /// </summary>
        /// <returns>The encoding version.</returns>
        public EncodingVersion GetEncoding()
        {
            return _encapsStack != null ? _encapsStack.encoding : _encoding;
        }

        /// <summary>
        /// Determines the size of the current encapsulation, excluding the encapsulation header.
        /// </summary>
        /// <returns>The size of the encapsulated data.</returns>
        public int GetEncapsulationSize()
        {
            Debug.Assert(_encapsStack != null);
            return _encapsStack.sz - 6;
        }

        /// <summary>
        /// Skips over an encapsulation.
        /// </summary>
        /// <returns>The encoding version of the skipped encapsulation.</returns>
        public EncodingVersion SkipEncapsulation()
        {
            int sz = ReadInt();
            if (sz < 6)
            {
                throw new UnmarshalOutOfBoundsException();
            }
            byte major = ReadByte();
            byte minor = ReadByte();
            EncodingVersion encoding = new EncodingVersion(major, minor);
            try
            {
                _buf.b.position(_buf.b.position() + sz - 6);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
            return encoding;
        }

        /// <summary>
        /// Reads the start of a class instance or exception slice.
        /// </summary>
        /// <returns>The Slice type ID for this slice.</returns>
        public string StartSlice() // Returns type ID of next slice
        {
            Debug.Assert(_encapsStack != null && _encapsStack.decoder != null);
            return _encapsStack.decoder.startSlice(true);
        }

        /// <summary>
        /// Indicates that the end of a class instance or exception slice has been reached.
        /// </summary>
        public void EndSlice()
        {
            Debug.Assert(_encapsStack != null && _encapsStack.decoder != null);
            _encapsStack.decoder.endSlice();
        }

        /// <summary>
        /// Skips over a class instance or exception slice.
        /// </summary>
        public void SkipSlice()
        {
            Debug.Assert(_encapsStack != null && _encapsStack.decoder != null);
            _encapsStack.decoder.skipSlice();
        }

        /// <summary>
        /// No-op, to be removed
        /// </summary>
        public void ReadPendingClasses()
        {
            // TODO: remove this method
        }

        /// <summary>
        /// Extracts a size from the stream.
        /// </summary>
        /// <returns>The extracted size.</returns>
        public int ReadSize()
        {
            try
            {
                byte b = _buf.b.get();
                if (b == 255)
                {
                    int v = _buf.b.getInt();
                    if (v < 0)
                    {
                        throw new UnmarshalOutOfBoundsException();
                    }
                    return v;
                }
                else
                {
                    return b; // byte is unsigned
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Reads a sequence size and make sure there is enough space in the underlying buffer to read the sequence.
        /// This validation is performed to make sure we do not allocate a large container based on an invalid encoded
        /// size.
        /// </summary>
        /// <param name="minElementSize">The minimum encoded size of an element of the sequence, in bytes.</param>
        /// <returns>The number of elements in the sequence.</returns>
        public int ReadAndCheckSeqSize(int minElementSize)
        {
            int sz = ReadSize();

            if (sz == 0)
            {
                return 0;
            }

            int minSize = sz * minElementSize;

            // With _minTotalSeqSize, we make sure that multiple sequences within an InpuStream can't trigger
            // maliciously the allocation of a large amount of memory before we read these sequences from the buffer.
            _minTotalSeqSize += minSize;

            if (_buf.b.position() + minSize > _buf.size() || _minTotalSeqSize > _buf.size())
            {
                throw new UnmarshalOutOfBoundsException();
            }
            return sz;
        }

        /// <summary>
        /// Reads a blob of bytes from the stream. The length of the given array determines how many bytes are read.
        /// </summary>
        /// <param name="v">Bytes from the stream.</param>
        public void ReadBlob(byte[] v)
        {
            try
            {
                _buf.b.get(v);
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Reads a blob of bytes from the stream.
        /// </summary>
        /// <param name="sz">The number of bytes to read.</param>
        /// <returns>The requested bytes as a byte array.</returns>
        public byte[] ReadBlob(int sz)
        {
            if (_buf.b.remaining() < sz)
            {
                throw new UnmarshalOutOfBoundsException();
            }
            byte[] v = new byte[sz];
            try
            {
                _buf.b.get(v);
                return v;
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Determine if an optional value is available for reading.
        /// </summary>
        /// <param name="tag">The tag associated with the value.</param>
        /// <param name="expectedFormat">The optional format for the value.</param>
        /// <returns>True if the value is present, false otherwise.</returns>
        public bool ReadOptional(int tag, OptionalFormat expectedFormat)
        {
            Debug.Assert(_encapsStack != null);
            if (_encapsStack.decoder != null)
            {
                return _encapsStack.decoder.readOptional(tag, expectedFormat);
            }
            else
            {
                return readOptImpl(tag, expectedFormat);
            }
        }

        /// <summary>
        /// Extracts a byte value from the stream.
        /// </summary>
        /// <returns>The extracted byte.</returns>
        public byte ReadByte()
        {
            try
            {
                return _buf.b.get();
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts an optional byte value from the stream.
        /// </summary>
        /// <param name="tag">The numeric tag associated with the value.</param>
        /// <returns>The optional value.</returns>
        public byte? ReadByte(int tag)
        {
            if (ReadOptional(tag, OptionalFormat.F1))
            {
                return ReadByte();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts a sequence of byte values from the stream.
        /// </summary>
        /// <returns>The extracted byte sequence.</returns>
        public byte[] ReadByteSeq()
        {
            try
            {
                int sz = ReadAndCheckSeqSize(1);
                byte[] v = new byte[sz];
                _buf.b.get(v);
                return v;
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts a sequence of byte values from the stream.
        /// </summary>
        /// <param name="l">The extracted byte sequence as a list.</param>
        public void ReadByteSeq(out List<byte> l)
        {
            //
            // Reading into an array and copy-constructing the
            // list is faster than constructing the list
            // and adding to it one element at a time.
            //
            l = new List<byte>(ReadByteSeq());
        }

        /// <summary>
        /// Extracts a sequence of byte values from the stream.
        /// </summary>
        /// <param name="l">The extracted byte sequence as a linked list.</param>
        public void ReadByteSeq(out LinkedList<byte> l)
        {
            //
            // Reading into an array and copy-constructing the
            // list is faster than constructing the list
            // and adding to it one element at a time.
            //
            l = new LinkedList<byte>(ReadByteSeq());
        }

        /// <summary>
        /// Extracts a sequence of byte values from the stream.
        /// </summary>
        /// <param name="l">The extracted byte sequence as a queue.</param>
        public void ReadByteSeq(out Queue<byte> l)
        {
            //
            // Reading into an array and copy-constructing the
            // queue is faster than constructing the queue
            // and adding to it one element at a time.
            //
            l = new Queue<byte>(ReadByteSeq());
        }

        /// <summary>
        /// Extracts a sequence of byte values from the stream.
        /// </summary>
        /// <param name="l">The extracted byte sequence as a stack.</param>
        public void ReadByteSeq(out Stack<byte> l)
        {
            //
            // Reverse the contents by copying into an array first
            // because the stack is marshaled in top-to-bottom order.
            //
            byte[] array = ReadByteSeq();
            Array.Reverse(array);
            l = new Stack<byte>(array);
        }

        /// <summary>
        /// Extracts an optional byte sequence from the stream.
        /// </summary>
        /// <param name="tag">The numeric tag associated with the value.</param>
        /// <returns>The optional value.</returns>
        public byte[]? ReadByteSeq(int tag)
        {
            if (ReadOptional(tag, OptionalFormat.VSize))
            {
                return ReadByteSeq();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts a serializable object from the stream.
        /// </summary>
        /// <returns>The serializable object.</returns>
        public object? ReadSerializable()
        {
            int sz = ReadAndCheckSeqSize(1);
            if (sz == 0)
            {
                return null;
            }
            try
            {
                var f = new BinaryFormatter(null, new StreamingContext(StreamingContextStates.All, _communicator));
                return f.Deserialize(new IceInternal.InputStreamWrapper(sz, this));
            }
            catch (System.Exception ex)
            {
                throw new MarshalException("cannot deserialize object:", ex);
            }
        }

        /// <summary>
        /// Extracts a boolean value from the stream.
        /// </summary>
        /// <returns>The extracted boolean.</returns>
        public bool ReadBool()
        {
            try
            {
                return _buf.b.get() == 1;
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts an optional boolean value from the stream.
        /// </summary>
        /// <param name="tag">The numeric tag associated with the value.</param>
        /// <returns>The optional value.</returns>
        public bool? ReadBool(int tag)
        {
            if (ReadOptional(tag, OptionalFormat.F1))
            {
                return ReadBool();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts a sequence of boolean values from the stream.
        /// </summary>
        /// <returns>The extracted boolean sequence.</returns>
        public bool[] ReadBoolSeq()
        {
            try
            {
                int sz = ReadAndCheckSeqSize(1);
                bool[] v = new bool[sz];
                _buf.b.getBoolSeq(v);
                return v;
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts a sequence of boolean values from the stream.
        /// </summary>
        /// <param name="l">The extracted boolean sequence as a list.</param>
        public void ReadBoolSeq(out List<bool> l)
        {
            //
            // Reading into an array and copy-constructing the
            // list is faster than constructing the list
            // and adding to it one element at a time.
            //
            l = new List<bool>(ReadBoolSeq());
        }

        /// <summary>
        /// Extracts a sequence of boolean values from the stream.
        /// </summary>
        /// <param name="l">The extracted boolean sequence as a linked list.</param>
        public void ReadBoolSeq(out LinkedList<bool> l)
        {
            //
            // Reading into an array and copy-constructing the
            // list is faster than constructing the list
            // and adding to it one element at a time.
            //
            l = new LinkedList<bool>(ReadBoolSeq());
        }

        /// <summary>
        /// Extracts a sequence of boolean values from the stream.
        /// </summary>
        /// <param name="l">The extracted boolean sequence as a queue.</param>
        public void ReadBoolSeq(out Queue<bool> l)
        {
            //
            // Reading into an array and copy-constructing the
            // queue is faster than constructing the queue
            // and adding to it one element at a time.
            //
            l = new Queue<bool>(ReadBoolSeq());
        }

        /// <summary>
        /// Extracts a sequence of boolean values from the stream.
        /// </summary>
        /// <param name="l">The extracted boolean sequence as a stack.</param>
        public void ReadBoolSeq(out Stack<bool> l)
        {
            //
            // Reverse the contents by copying into an array first
            // because the stack is marshaled in top-to-bottom order.
            //
            bool[] array = ReadBoolSeq();
            Array.Reverse(array);
            l = new Stack<bool>(array);
        }

        /// <summary>
        /// Extracts an optional boolean sequence from the stream.
        /// </summary>
        /// <param name="tag">The numeric tag associated with the value.</param>
        /// <returns>The optional value.</returns>
        public bool[]? ReadBoolSeq(int tag)
        {
            if (ReadOptional(tag, OptionalFormat.VSize))
            {
                return ReadBoolSeq();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts a short value from the stream.
        /// </summary>
        /// <returns>The extracted short.</returns>
        public short ReadShort()
        {
            try
            {
                return _buf.b.getShort();
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts an optional short value from the stream.
        /// </summary>
        /// <param name="tag">The numeric tag associated with the value.</param>
        /// <returns>The optional value.</returns>
        public short? ReadShort(int tag)
        {
            if (ReadOptional(tag, OptionalFormat.F2))
            {
                return ReadShort();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts a sequence of short values from the stream.
        /// </summary>
        /// <returns>The extracted short sequence.</returns>
        public short[] ReadShortSeq()
        {
            try
            {
                int sz = ReadAndCheckSeqSize(2);
                short[] v = new short[sz];
                _buf.b.getShortSeq(v);
                return v;
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts a sequence of short values from the stream.
        /// </summary>
        /// <param name="l">The extracted short sequence as a list.</param>
        public void ReadShortSeq(out List<short> l)
        {
            //
            // Reading into an array and copy-constructing the
            // list is faster than constructing the list
            // and adding to it one element at a time.
            //
            l = new List<short>(ReadShortSeq());
        }

        /// <summary>
        /// Extracts a sequence of short values from the stream.
        /// </summary>
        /// <param name="l">The extracted short sequence as a linked list.</param>
        public void ReadShortSeq(out LinkedList<short> l)
        {
            //
            // Reading into an array and copy-constructing the
            // list is faster than constructing the list
            // and adding to it one element at a time.
            //
            l = new LinkedList<short>(ReadShortSeq());
        }

        /// <summary>
        /// Extracts a sequence of short values from the stream.
        /// </summary>
        /// <param name="l">The extracted short sequence as a queue.</param>
        public void ReadShortSeq(out Queue<short> l)
        {
            //
            // Reading into an array and copy-constructing the
            // queue is faster than constructing the queue
            // and adding to it one element at a time.
            //
            l = new Queue<short>(ReadShortSeq());
        }

        /// <summary>
        /// Extracts a sequence of short values from the stream.
        /// </summary>
        /// <param name="l">The extracted short sequence as a stack.</param>
        public void ReadShortSeq(out Stack<short> l)
        {
            //
            // Reverse the contents by copying into an array first
            // because the stack is marshaled in top-to-bottom order.
            //
            short[] array = ReadShortSeq();
            Array.Reverse(array);
            l = new Stack<short>(array);
        }

        /// <summary>
        /// Extracts an optional short sequence from the stream.
        /// </summary>
        /// <param name="tag">The numeric tag associated with the value.</param>
        /// <returns>The optional value.</returns>
        public short[]? ReadShortSeq(int tag)
        {
            if (ReadOptional(tag, OptionalFormat.VSize))
            {
                skipSize();
                return ReadShortSeq();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts an int value from the stream.
        /// </summary>
        /// <returns>The extracted int.</returns>
        public int ReadInt()
        {
            try
            {
                return _buf.b.getInt();
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts an optional int value from the stream.
        /// </summary>
        /// <param name="tag">The numeric tag associated with the value.</param>
        /// <returns>The optional value.</returns>
        public int? ReadInt(int tag)
        {
            if (ReadOptional(tag, OptionalFormat.F4))
            {
                return ReadInt();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts a sequence of int values from the stream.
        /// </summary>
        /// <returns>The extracted int sequence.</returns>
        public int[] ReadIntSeq()
        {
            try
            {
                int sz = ReadAndCheckSeqSize(4);
                int[] v = new int[sz];
                _buf.b.getIntSeq(v);
                return v;
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts a sequence of int values from the stream.
        /// </summary>
        /// <param name="l">The extracted int sequence as a list.</param>
        public void ReadIntSeq(out List<int> l)
        {
            //
            // Reading into an array and copy-constructing the
            // list is faster than constructing the list
            // and adding to it one element at a time.
            //
            l = new List<int>(ReadIntSeq());
        }

        /// <summary>
        /// Extracts a sequence of int values from the stream.
        /// </summary>
        /// <param name="l">The extracted int sequence as a linked list.</param>
        public void ReadIntSeq(out LinkedList<int> l)
        {
            try
            {
                int sz = ReadAndCheckSeqSize(4);
                l = new LinkedList<int>();
                for (int i = 0; i < sz; ++i)
                {
                    l.AddLast(_buf.b.getInt());
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts a sequence of int values from the stream.
        /// </summary>
        /// <param name="l">The extracted int sequence as a queue.</param>
        public void ReadIntSeq(out Queue<int> l)
        {
            //
            // Reading into an array and copy-constructing the
            // queue takes the same time as constructing the queue
            // and adding to it one element at a time, so
            // we avoid the copy.
            //
            try
            {
                int sz = ReadAndCheckSeqSize(4);
                l = new Queue<int>(sz);
                for (int i = 0; i < sz; ++i)
                {
                    l.Enqueue(_buf.b.getInt());
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts a sequence of int values from the stream.
        /// </summary>
        /// <param name="l">The extracted int sequence as a stack.</param>
        public void ReadIntSeq(out Stack<int> l)
        {
            //
            // Reverse the contents by copying into an array first
            // because the stack is marshaled in top-to-bottom order.
            //
            int[] array = ReadIntSeq();
            Array.Reverse(array);
            l = new Stack<int>(array);
        }

        /// <summary>
        /// Extracts an optional int sequence from the stream.
        /// </summary>
        /// <param name="tag">The numeric tag associated with the value.</param>
        /// <returns>The optional value.</returns>
        public int[]? ReadIntSeq(int tag)
        {
            if (ReadOptional(tag, OptionalFormat.VSize))
            {
                skipSize();
                return ReadIntSeq();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts a long value from the stream.
        /// </summary>
        /// <returns>The extracted long.</returns>
        public long ReadLong()
        {
            try
            {
                return _buf.b.getLong();
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts an optional long value from the stream.
        /// </summary>
        /// <param name="tag">The numeric tag associated with the value.</param>
        /// <returns>The optional value.</returns>
        public long? ReadLong(int tag)
        {
            if (ReadOptional(tag, OptionalFormat.F8))
            {
                return ReadLong();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts a sequence of long values from the stream.
        /// </summary>
        /// <returns>The extracted long sequence.</returns>
        public long[] ReadLongSeq()
        {
            try
            {
                int sz = ReadAndCheckSeqSize(8);
                long[] v = new long[sz];
                _buf.b.getLongSeq(v);
                return v;
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts a sequence of long values from the stream.
        /// </summary>
        /// <param name="l">The extracted long sequence as a list.</param>
        public void ReadLongSeq(out List<long> l)
        {
            //
            // Reading into an array and copy-constructing the
            // list is faster than constructing the list
            // and adding to it one element at a time.
            //
            l = new List<long>(ReadLongSeq());
        }

        /// <summary>
        /// Extracts a sequence of long values from the stream.
        /// </summary>
        /// <param name="l">The extracted long sequence as a linked list.</param>
        public void ReadLongSeq(out LinkedList<long> l)
        {
            try
            {
                int sz = ReadAndCheckSeqSize(4);
                l = new LinkedList<long>();
                for (int i = 0; i < sz; ++i)
                {
                    l.AddLast(_buf.b.getLong());
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts a sequence of long values from the stream.
        /// </summary>
        /// <param name="l">The extracted long sequence as a queue.</param>
        public void ReadLongSeq(out Queue<long> l)
        {
            //
            // Reading into an array and copy-constructing the
            // queue takes the same time as constructing the queue
            // and adding to it one element at a time, so
            // we avoid the copy.
            //
            try
            {
                int sz = ReadAndCheckSeqSize(4);
                l = new Queue<long>(sz);
                for (int i = 0; i < sz; ++i)
                {
                    l.Enqueue(_buf.b.getLong());
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts a sequence of long values from the stream.
        /// </summary>
        /// <param name="l">The extracted long sequence as a stack.</param>
        public void ReadLongSeq(out Stack<long> l)
        {
            //
            // Reverse the contents by copying into an array first
            // because the stack is marshaled in top-to-bottom order.
            //
            long[] array = ReadLongSeq();
            Array.Reverse(array);
            l = new Stack<long>(array);
        }

        /// <summary>
        /// Extracts an optional long sequence from the stream.
        /// </summary>
        /// <param name="tag">The numeric tag associated with the value.</param>
        /// <returns>The optional value.</returns>
        public long[]? ReadLongSeq(int tag)
        {
            if (ReadOptional(tag, OptionalFormat.VSize))
            {
                skipSize();
                return ReadLongSeq();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts a float value from the stream.
        /// </summary>
        /// <returns>The extracted float.</returns>
        public float ReadFloat()
        {
            try
            {
                return _buf.b.getFloat();
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts an optional float value from the stream.
        /// </summary>
        /// <param name="tag">The numeric tag associated with the value.</param>
        /// <returns>The optional value.</returns>
        public float? ReadFloat(int tag)
        {
            if (ReadOptional(tag, OptionalFormat.F4))
            {
                return ReadFloat();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts a sequence of float values from the stream.
        /// </summary>
        /// <returns>The extracted float sequence.</returns>
        public float[] ReadFloatSeq()
        {
            try
            {
                int sz = ReadAndCheckSeqSize(4);
                float[] v = new float[sz];
                _buf.b.getFloatSeq(v);
                return v;
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts a sequence of float values from the stream.
        /// </summary>
        /// <param name="l">The extracted float sequence as a list.</param>
        public void ReadFloatSeq(out List<float> l)
        {
            //
            // Reading into an array and copy-constructing the
            // list is faster than constructing the list
            // and adding to it one element at a time.
            //
            l = new List<float>(ReadFloatSeq());
        }

        /// <summary>
        /// Extracts a sequence of float values from the stream.
        /// </summary>
        /// <param name="l">The extracted float sequence as a linked list.</param>
        public void ReadFloatSeq(out LinkedList<float> l)
        {
            try
            {
                int sz = ReadAndCheckSeqSize(4);
                l = new LinkedList<float>();
                for (int i = 0; i < sz; ++i)
                {
                    l.AddLast(_buf.b.getFloat());
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts a sequence of float values from the stream.
        /// </summary>
        /// <param name="l">The extracted float sequence as a queue.</param>
        public void ReadFloatSeq(out Queue<float> l)
        {
            //
            // Reading into an array and copy-constructing the
            // queue takes the same time as constructing the queue
            // and adding to it one element at a time, so
            // we avoid the copy.
            //
            try
            {
                int sz = ReadAndCheckSeqSize(4);
                l = new Queue<float>(sz);
                for (int i = 0; i < sz; ++i)
                {
                    l.Enqueue(_buf.b.getFloat());
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts a sequence of float values from the stream.
        /// </summary>
        /// <param name="l">The extracted float sequence as a stack.</param>
        public void ReadFloatSeq(out Stack<float> l)
        {
            //
            // Reverse the contents by copying into an array first
            // because the stack is marshaled in top-to-bottom order.
            //
            float[] array = ReadFloatSeq();
            Array.Reverse(array);
            l = new Stack<float>(array);
        }

        /// <summary>
        /// Extracts an optional float sequence from the stream.
        /// </summary>
        /// <param name="tag">The numeric tag associated with the value.</param>
        /// <returns>The optional value.</returns>
        public float[]? ReadFloatSeq(int tag)
        {
            if (ReadOptional(tag, OptionalFormat.VSize))
            {
                skipSize();
                return ReadFloatSeq();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts a double value from the stream.
        /// </summary>
        /// <returns>The extracted double.</returns>
        public double ReadDouble()
        {
            try
            {
                return _buf.b.getDouble();
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts an optional double value from the stream.
        /// </summary>
        /// <param name="tag">The numeric tag associated with the value.</param>
        /// <returns>The optional value.</returns>
        public double? ReadDouble(int tag)
        {
            if (ReadOptional(tag, OptionalFormat.F8))
            {
                return ReadDouble();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts a sequence of double values from the stream.
        /// </summary>
        /// <returns>The extracted double sequence.</returns>
        public double[] ReadDoubleSeq()
        {
            try
            {
                int sz = ReadAndCheckSeqSize(8);
                double[] v = new double[sz];
                _buf.b.getDoubleSeq(v);
                return v;
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts a sequence of double values from the stream.
        /// </summary>
        /// <param name="l">The extracted double sequence as a list.</param>
        public void ReadDoubleSeq(out List<double> l)
        {
            //
            // Reading into an array and copy-constructing the
            // list is faster than constructing the list
            // and adding to it one element at a time.
            //
            l = new List<double>(ReadDoubleSeq());
        }

        /// <summary>
        /// Extracts a sequence of double values from the stream.
        /// </summary>
        /// <param name="l">The extracted double sequence as a linked list.</param>
        public void ReadDoubleSeq(out LinkedList<double> l)
        {
            try
            {
                int sz = ReadAndCheckSeqSize(4);
                l = new LinkedList<double>();
                for (int i = 0; i < sz; ++i)
                {
                    l.AddLast(_buf.b.getDouble());
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts a sequence of double values from the stream.
        /// </summary>
        /// <param name="l">The extracted double sequence as a queue.</param>
        public void ReadDoubleSeq(out Queue<double> l)
        {
            //
            // Reading into an array and copy-constructing the
            // queue takes the same time as constructing the queue
            // and adding to it one element at a time, so
            // we avoid the copy.
            //
            try
            {
                int sz = ReadAndCheckSeqSize(4);
                l = new Queue<double>(sz);
                for (int i = 0; i < sz; ++i)
                {
                    l.Enqueue(_buf.b.getDouble());
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
        }

        /// <summary>
        /// Extracts a sequence of double values from the stream.
        /// </summary>
        /// <param name="l">The extracted double sequence as a stack.</param>
        public void ReadDoubleSeq(out Stack<double> l)
        {
            //
            // Reverse the contents by copying into an array first
            // because the stack is marshaled in top-to-bottom order.
            //
            double[] array = ReadDoubleSeq();
            Array.Reverse(array);
            l = new Stack<double>(array);
        }

        /// <summary>
        /// Extracts an optional double sequence from the stream.
        /// </summary>
        /// <param name="tag">The numeric tag associated with the value.</param>
        /// <returns>The optional value.</returns>
        public double[]? ReadDoubleSeq(int tag)
        {
            if (ReadOptional(tag, OptionalFormat.VSize))
            {
                skipSize();
                return ReadDoubleSeq();
            }
            else
            {
                return null;
            }
        }

        private static System.Text.UTF8Encoding utf8 = new System.Text.UTF8Encoding(false, true);

        /// <summary>
        /// Extracts a string from the stream.
        /// </summary>
        /// <returns>The extracted string.</returns>
        public string ReadString()
        {
            int len = ReadSize();

            if (len == 0)
            {
                return "";
            }

            //
            // Check the buffer has enough bytes to read.
            //
            if (_buf.b.remaining() < len)
            {
                throw new UnmarshalOutOfBoundsException();
            }

            try
            {
                //
                // We reuse the _stringBytes array to avoid creating
                // excessive garbage
                //
                if (_stringBytes == null || len > _stringBytes.Length)
                {
                    _stringBytes = new byte[len];
                }
                _buf.b.get(_stringBytes, 0, len);
                return utf8.GetString(_stringBytes, 0, len);
            }
            catch (InvalidOperationException ex)
            {
                throw new UnmarshalOutOfBoundsException(ex);
            }
            catch (ArgumentException ex)
            {
                throw new MarshalException("Invalid UTF8 string", ex);
            }
        }

        /// <summary>
        /// Extracts an optional string from the stream.
        /// </summary>
        /// <param name="tag">The numeric tag associated with the value.</param>
        /// <returns>The optional value.</returns>
        public string? ReadString(int tag)
        {
            if (ReadOptional(tag, OptionalFormat.VSize))
            {
                return ReadString();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts a sequence of strings from the stream.
        /// </summary>
        /// <returns>The extracted string sequence.</returns>
        public string[] ReadStringSeq()
        {
            int sz = ReadAndCheckSeqSize(1);
            string[] v = new string[sz];
            for (int i = 0; i < sz; i++)
            {
                v[i] = ReadString();
            }
            return v;
        }

        /// <summary>
        /// Extracts a sequence of strings from the stream.
        /// </summary>
        /// <param name="l">The extracted string sequence as a list.</param>
        public void ReadStringSeq(out List<string> l)
        {
            //
            // Reading into an array and copy-constructing the
            // list is slower than constructing the list
            // and adding to it one element at a time.
            //
            int sz = ReadAndCheckSeqSize(1);
            l = new List<string>(sz);
            for (int i = 0; i < sz; ++i)
            {
                l.Add(ReadString());
            }
        }

        /// <summary>
        /// Extracts a sequence of strings from the stream.
        /// </summary>
        /// <param name="l">The extracted string sequence as a linked list.</param>
        public void ReadStringSeq(out LinkedList<string> l)
        {
            //
            // Reading into an array and copy-constructing the
            // list is slower than constructing the list
            // and adding to it one element at a time.
            //
            int sz = ReadAndCheckSeqSize(1);
            l = new LinkedList<string>();
            for (int i = 0; i < sz; ++i)
            {
                l.AddLast(ReadString());
            }
        }

        /// <summary>
        /// Extracts a sequence of strings from the stream.
        /// </summary>
        /// <param name="l">The extracted string sequence as a queue.</param>
        public void ReadStringSeq(out Queue<string> l)
        {
            //
            // Reading into an array and copy-constructing the
            // queue is slower than constructing the queue
            // and adding to it one element at a time.
            //
            int sz = ReadAndCheckSeqSize(1);
            l = new Queue<string>();
            for (int i = 0; i < sz; ++i)
            {
                l.Enqueue(ReadString());
            }
        }

        /// <summary>
        /// Extracts a sequence of strings from the stream.
        /// </summary>
        /// <param name="l">The extracted string sequence as a stack.</param>
        public void ReadStringSeq(out Stack<string> l)
        {
            //
            // Reverse the contents by copying into an array first
            // because the stack is marshaled in top-to-bottom order.
            //
            string[] array = ReadStringSeq();
            Array.Reverse(array);
            l = new Stack<string>(array);
        }

        /// <summary>
        /// Extracts an optional string sequence from the stream.
        /// </summary>
        /// <param name="tag">The numeric tag associated with the value.</param>
        /// <returns>The optional value.</returns>
        public string[]? ReadStringSeq(int tag)
        {
            if (ReadOptional(tag, OptionalFormat.FSize))
            {
                skip(4);
                return ReadStringSeq();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts a proxy from the stream. The stream must have been initialized with a communicator.
        /// </summary>
        /// <returns>The extracted proxy.</returns>
        public T? ReadProxy<T>(ProxyFactory<T> factory) where T : class, IObjectPrx
        {
            Identity ident = new Identity();
            ident.ice_readMembers(this);
            if (ident.Name.Length == 0)
            {
                return null;
            }
            else
            {
                return factory(_communicator.CreateReference(ident, this));
            }
        }

        /// <summary>
        /// Extracts an optional proxy from the stream. The stream must have been initialized with a communicator.
        /// </summary>
        /// <param name="tag">The numeric tag associated with the value.</param>
        /// <param name="factory">The proxy factory used to create the typed proxy.</param>
        /// <returns>The optional value.</returns>
        public T? ReadProxy<T>(int tag, ProxyFactory<T> factory) where T : class, IObjectPrx
        {
            if (ReadOptional(tag, OptionalFormat.FSize))
            {
                skip(4);
                return ReadProxy(factory);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Read an enumerated value.
        /// </summary>
        /// <param name="maxValue">The maximum enumerator value in the definition.</param>
        /// <returns>The enumerator.</returns>
        public int ReadEnum(int maxValue)
        {
            if (GetEncoding().Equals(Util.Encoding_1_0))
            {
                if (maxValue < 127)
                {
                    return ReadByte();
                }
                else if (maxValue < 32767)
                {
                    return ReadShort();
                }
                else
                {
                    return ReadInt();
                }
            }
            else
            {
                return ReadSize();
            }
        }

        /// <summary>
        /// Read an instance of class T.
        /// </summary>
        /// <returns>The class instance, or null.</returns>
        public T? ReadClass<T>() where T : AnyClass
        {
            var obj = ReadAnyClass();
            if (obj == null)
            {
                return null;
            }
            else if (obj is T)
            {
                return (T)obj;
            }
            else
            {
                IceInternal.Ex.throwUOE(typeof(T), obj);
                return null;
            }
        }

        /// <summary>
        /// Read an instance of a class.
        /// </summary>
        /// <returns>The class instance, or null.</returns>
        private AnyClass? ReadAnyClass()
        {
            initEncaps();
            return _encapsStack.decoder.readClass();
        }

        /// <summary>
        /// Read a tagged parameter or data member of type class T.
        /// </summary>
        /// <param name="tag">The numeric tag associated with the class parameter or data member.</param>
        /// <returns>The class instance, or null.</returns>
        public T? ReadClass<T>(int tag) where T : AnyClass
        {
            var obj = ReadAnyClass(tag);
            if (obj == null)
            {
                return null;
            }
            else if (obj is T)
            {
                return (T)obj;
            }
            else
            {
                IceInternal.Ex.throwUOE(typeof(T), obj);
                return null;
            }
        }

        /// <summary>
        /// Read a tagged parameter or data member of type class.
        /// </summary>
        /// <param name="tag">The numeric tag associated with the class parameter or data member.</param>
        /// <returns>The class instance, or null.</returns>
        private AnyClass? ReadAnyClass(int tag)
        {
            if (ReadOptional(tag, OptionalFormat.Class))
            {
                return ReadAnyClass();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts a user exception from the stream and throws it.
        /// </summary>
        public void ThrowException()
        {
            ThrowException(null);
        }

        /// <summary>
        /// Extracts a user exception from the stream and throws it.
        /// </summary>
        /// <param name="factory">The user exception factory, or null to use the stream's default behavior.</param>
        public void ThrowException(UserExceptionFactory? factory)
        {
            initEncaps();
            _encapsStack.decoder.throwException(factory);
        }

        /// <summary>
        /// Skip the given number of bytes.
        /// </summary>
        /// <param name="size">The number of bytes to skip</param>
        public void skip(int size)
        {
            if (size < 0 || size > _buf.b.remaining())
            {
                throw new UnmarshalOutOfBoundsException();
            }
            _buf.b.position(_buf.b.position() + size);
        }

        /// <summary>
        /// Skip over a size value.
        /// </summary>
        public void skipSize()
        {
            byte b = ReadByte();
            if (b == 255)
            {
                skip(4);
            }
        }

        /// <summary>
        /// Determines the current position in the stream.
        /// </summary>
        /// <returns>The current position.</returns>
        public int pos()
        {
            return _buf.b.position();
        }

        /// <summary>
        /// Sets the current position in the stream.
        /// </summary>
        /// <param name="n">The new position.</param>
        public void pos(int n)
        {
            _buf.b.position(n);
        }

        /// <summary>
        /// Determines the current size of the stream.
        /// </summary>
        /// <returns>The current size.</returns>
        public int size()
        {
            return _buf.size();
        }

        /// <summary>
        /// Determines whether the stream is empty.
        /// </summary>
        /// <returns>True if the internal buffer has no data, false otherwise.</returns>
        public bool isEmpty()
        {
            return _buf.empty();
        }

        private bool readOptImpl(int readTag, OptionalFormat expectedFormat)
        {
            if (isEncoding_1_0())
            {
                return false; // Optional members aren't supported with the 1.0 encoding.
            }

            while (true)
            {
                if (_buf.b.position() >= _encapsStack.start + _encapsStack.sz)
                {
                    return false; // End of encapsulation also indicates end of optionals.
                }

                int v = ReadByte();
                if (v == Protocol.OPTIONAL_END_MARKER)
                {
                    _buf.b.position(_buf.b.position() - 1); // Rewind.
                    return false;
                }

                OptionalFormat format = (OptionalFormat)(v & 0x07); // First 3 bits.
                int tag = v >> 3;
                if (tag == 30)
                {
                    tag = ReadSize();
                }

                if (tag > readTag)
                {
                    int offset = tag < 30 ? 1 : (tag < 255 ? 2 : 6); // Rewind
                    _buf.b.position(_buf.b.position() - offset);
                    return false; // No optional data members with the requested tag.
                }
                else if (tag < readTag)
                {
                    skipOptional(format); // Skip optional data members
                }
                else
                {
                    if (format != expectedFormat)
                    {
                        throw new MarshalException("invalid optional data member `" + tag + "': unexpected format");
                    }
                    return true;
                }
            }
        }

        private void skipOptional(OptionalFormat format)
        {
            switch (format)
            {
                case OptionalFormat.F1:
                    {
                        skip(1);
                        break;
                    }
                case OptionalFormat.F2:
                    {
                        skip(2);
                        break;
                    }
                case OptionalFormat.F4:
                    {
                        skip(4);
                        break;
                    }
                case OptionalFormat.F8:
                    {
                        skip(8);
                        break;
                    }
                case OptionalFormat.Size:
                    {
                        skipSize();
                        break;
                    }
                case OptionalFormat.VSize:
                    {
                        skip(ReadSize());
                        break;
                    }
                case OptionalFormat.FSize:
                    {
                        skip(ReadInt());
                        break;
                    }
                case OptionalFormat.Class:
                    {
                        ReadAnyClass();
                        break;
                    }
            }
        }

        private bool skipOptionals()
        {
            //
            // Skip remaining un-read optional members.
            //
            while (true)
            {
                if (_buf.b.position() >= _encapsStack.start + _encapsStack.sz)
                {
                    return false; // End of encapsulation also indicates end of optionals.
                }

                int v = ReadByte();
                if (v == Protocol.OPTIONAL_END_MARKER)
                {
                    return true;
                }

                OptionalFormat format = (OptionalFormat)(v & 0x07); // Read first 3 bits.
                if ((v >> 3) == 30)
                {
                    skipSize();
                }
                skipOptional(format);
            }
        }

        private UserException? createUserException(string id)
        {
            UserException? userEx = null;

            try
            {
                if (_classResolver != null)
                {
                    Type c = _classResolver(id);
                    if (c != null)
                    {
                        Debug.Assert(!c.IsAbstract && !c.IsInterface);
                        userEx = (UserException?)IceInternal.AssemblyUtil.createInstance(c);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new MarshalException(ex);
            }

            return userEx;
        }

        private Communicator _communicator;
        private IceInternal.Buffer _buf;
        private object? _closure;
        private byte[] _stringBytes; // Reusable array for reading strings.

        private enum SliceType { ClassSlice, ExceptionSlice }

        private abstract class EncapsDecoder
        {
            internal EncapsDecoder(InputStream stream, Encaps encaps, bool sliceClasses,
                                   int classGraphDepthMax, System.Func<string, Type> cr)
            {
                _stream = stream;
                _encaps = encaps;
                _sliceClasses = sliceClasses;
                _classGraphDepthMax = classGraphDepthMax;
                _classGraphDepth = 0;
                _classResolver = cr;
                _typeIdIndex = 0;
                _unmarshaledMap = new Dictionary<int, AnyClass>();
            }

            internal abstract AnyClass? readClass();
            internal abstract void throwException(UserExceptionFactory? factory);

            internal abstract void startInstance(SliceType type);
            internal abstract SlicedData endInstance(bool preserve);
            internal abstract string startSlice(bool readIndirectionTable);
            internal abstract void endSlice();
            internal abstract void skipSlice();

            internal virtual bool readOptional(int tag, OptionalFormat format)
            {
                return false;
            }

            protected string readTypeId(bool isIndex)
            {
                _typeIdMap ??= new Dictionary<int, string>();

                if (isIndex)
                {
                    int index = _stream.ReadSize();
                    string typeId;
                    if (!_typeIdMap.TryGetValue(index, out typeId))
                    {
                        throw new UnmarshalOutOfBoundsException();
                    }
                    return typeId;
                }
                else
                {
                    string typeId = _stream.ReadString();

                    // We only want to insert this typeId in the map and increment the index
                    // when it's the first time we read it, so we save the largest pos we
                    // read to figure when to insert.
                    if (_stream.pos() > _posAfterLatestInsertedTypeId)
                    {
                        _posAfterLatestInsertedTypeId = _stream.pos();
                        _typeIdMap.Add(++_typeIdIndex, typeId);
                    }

                    return typeId;
                }
            }

            protected Type resolveClass(string typeId)
            {
                Type cls = null;
                if (_typeIdCache == null)
                {
                    _typeIdCache = new Dictionary<string, Type>(); // Lazy initialization.
                }
                else
                {
                    _typeIdCache.TryGetValue(typeId, out cls);
                }

                if (cls == typeof(EncapsDecoder)) // Marker for non-existent class.
                {
                    cls = null;
                }
                else if (cls == null)
                {
                    try
                    {
                        if (_classResolver != null)
                        {
                            cls = _classResolver(typeId);
                            _typeIdCache.Add(typeId, cls != null ? cls : typeof(EncapsDecoder));
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new NoClassFactoryException("no class factory", typeId, ex);
                    }
                }

                return cls;
            }

            protected AnyClass? newInstance(string typeId)
            {
                AnyClass? v = null;

                Type cls = resolveClass(typeId);

                if (cls != null)
                {
                    try
                    {
                        Debug.Assert(!cls.IsAbstract && !cls.IsInterface);
                        v = (AnyClass?)IceInternal.AssemblyUtil.createInstance(cls);
                    }
                    catch (Exception ex)
                    {
                        throw new NoClassFactoryException("no class factory", typeId, ex);
                    }
                }

                return v;
            }

            protected readonly InputStream _stream;
            protected readonly Encaps _encaps;
            protected readonly bool _sliceClasses;
            protected readonly int _classGraphDepthMax;
            protected int _classGraphDepth;
            protected System.Func<string, Type> _classResolver;

            //
            // Encapsulation attributes for object unmarshaling.
            //
            protected Dictionary<int, AnyClass> _unmarshaledMap;
            private Dictionary<int, string> _typeIdMap;
            private int _typeIdIndex;
            private int _posAfterLatestInsertedTypeId = 0;
            private Dictionary<string, Type> _typeIdCache;
        }

        private sealed class EncapsDecoder11 : EncapsDecoder
        {
            internal EncapsDecoder11(InputStream stream, Encaps encaps, bool sliceClasses, int classGraphDepthMax,
                                     System.Func<string, Type> cr, System.Func<int, string> r)
                : base(stream, encaps, sliceClasses, classGraphDepthMax, cr)
            {
                _compactIdResolver = r;
                _current = null;
                _valueIdIndex = 1;
            }

            internal override AnyClass? readClass()
            {
                int index = _stream.ReadSize();
                if (index < 0)
                {
                    throw new MarshalException("invalid object id");
                }
                else if (index == 0)
                {
                    return null;
                }
                else if (_current != null && (_current.sliceFlags & Protocol.FLAG_HAS_INDIRECTION_TABLE) != 0)
                {
                    //
                    // When reading an instance within a slice and there is an
                    // indirection table, we have an index within this indirection table.
                    //
                    // We need to decrement index since position 0 in the indirection table
                    // corresponds to index 1.
                    index--;
                    if (index < _current.IndirectionTable?.Length)
                    {
                        return _current.IndirectionTable[index];
                    }
                    else
                    {
                        throw new MarshalException("index too big for indirection table");
                    }
                }
                else
                {
                    return readInstance(index);
                }
            }

            internal override void throwException(UserExceptionFactory factory)
            {
                Debug.Assert(_current == null);

                push(SliceType.ExceptionSlice);

                //
                // Read the first slice header.
                //
                startSlice(true); // we read the indirection table immediately

                string mostDerivedId = _current.typeId;
                while (true)
                {
                    UserException userEx = null;

                    //
                    // Use a factory if one was provided.
                    //
                    if (factory != null)
                    {
                        try
                        {
                            factory(_current.typeId);
                        }
                        catch (UserException ex)
                        {
                            userEx = ex;
                        }
                    }

                    if (userEx == null)
                    {
                        userEx = _stream.createUserException(_current.typeId);
                    }

                    //
                    // We found the exception.
                    //
                    if (userEx != null)
                    {
                        userEx.iceRead(_stream);
                        throw userEx;

                        // Never reached.
                    }

                    //
                    // Slice off what we don't understand.
                    //
                    skipSlice();

                    if ((_current.sliceFlags & Protocol.FLAG_IS_LAST_SLICE) != 0)
                    {
                        if (mostDerivedId.StartsWith("::", StringComparison.Ordinal))
                        {
                            throw new UnknownUserException(mostDerivedId.Substring(2));
                        }
                        else
                        {
                            throw new UnknownUserException(mostDerivedId);
                        }
                    }

                    startSlice(true);
                }
            }

            internal override void startInstance(SliceType sliceType)
            {
                Debug.Assert(_current.sliceType == sliceType);
                _current.skipFirstSlice = true;
            }

            internal override SlicedData endInstance(bool preserve)
            {
                SlicedData slicedData = null;
                if (preserve)
                {
                    slicedData = readSlicedData();
                }

                // We may reuse this instance data (current) so we need to clean it well (see push)
                _current.slices?.Clear();
                _current.IndirectionTableList?.Clear();
                Debug.Assert(_current.DeferredIndirectionTableList == null ||
                    _current.DeferredIndirectionTableList.Count == 0);
                _current = _current.previous;
                return slicedData;
            }

            internal override string startSlice(bool readIndirectionTable)
            {
                //
                // If first slice, don't read the header, it was already read in
                // readInstance or throwException to find the factory.
                //
                if (_current.skipFirstSlice)
                {
                    _current.skipFirstSlice = false;
                }
                else
                {
                    _current.sliceFlags = _stream.ReadByte();

                    //
                    // Read the type ID, for instance slices the type ID is encoded as a
                    // string or as an index, for exceptions it's always encoded as a
                    // string.
                    //
                    if (_current.sliceType == SliceType.ClassSlice)
                    {
                        //
                        // Must be checked first!
                        //
                        if ((_current.sliceFlags & Protocol.FLAG_HAS_TYPE_ID_COMPACT) ==
                            Protocol.FLAG_HAS_TYPE_ID_COMPACT)
                        {
                            _current.typeId = "";
                            _current.compactId = _stream.ReadSize();
                        }
                        else if ((_current.sliceFlags &
                                (Protocol.FLAG_HAS_TYPE_ID_INDEX | Protocol.FLAG_HAS_TYPE_ID_STRING)) != 0)
                        {
                            _current.typeId = readTypeId((_current.sliceFlags & Protocol.FLAG_HAS_TYPE_ID_INDEX) != 0);
                            _current.compactId = -1;
                        }
                        else
                        {
                            // Only the most derived slice encodes the type ID for the compact format.
                            _current.typeId = "";
                            _current.compactId = -1;
                        }
                    }
                    else
                    {
                        _current.typeId = _stream.ReadString();
                        _current.compactId = -1;
                    }

                    //
                    // Read the slice size if necessary.
                    //
                    if ((_current.sliceFlags & Protocol.FLAG_HAS_SLICE_SIZE) != 0)
                    {
                        _current.sliceSize = _stream.ReadInt();
                        if (_current.sliceSize < 4)
                        {
                            throw new MarshalException("invalid slice size");
                        }
                    }
                    else
                    {
                        _current.sliceSize = 0;
                    }
                }

                // Read the indirection table now
                if (readIndirectionTable && (_current.sliceFlags & Protocol.FLAG_HAS_INDIRECTION_TABLE) != 0)
                {
                    if (_current.IndirectionTable != null)
                    {
                        // We already read it (skipFirstSlice was true and it's an exception), so nothing to do
                        // Note that for classes, we only read the indirection table for the first slice
                        // when skipFirstSlice is true
                    }
                    else
                    {
                        int savedPos = _stream.pos();
                        if (_current.sliceSize < 4)
                        {
                            throw new MarshalException("invalid slice size");
                        }
                        _stream.pos(savedPos + _current.sliceSize - 4);
                        _current.IndirectionTable = ReadIndirectionTable();
                        _current.PosAfterIndirectionTable = _stream.pos();
                        _stream.pos(savedPos);
                    }
                }

                return _current.typeId;
            }

            internal override void endSlice()
            {
                if ((_current.sliceFlags & Protocol.FLAG_HAS_OPTIONAL_MEMBERS) != 0)
                {
                    _stream.skipOptionals();
                }
                if ((_current.sliceFlags & Protocol.FLAG_HAS_INDIRECTION_TABLE) != 0)
                {
                    Debug.Assert(_current.PosAfterIndirectionTable.HasValue && _current.IndirectionTable != null);
                    _stream.pos(_current.PosAfterIndirectionTable.Value);
                    _current.PosAfterIndirectionTable = null;
                    _current.IndirectionTable = null;
                }
            }

            internal override void skipSlice()
            {
                if (_stream.Communicator().traceLevels().slicing > 0)
                {
                    ILogger logger = _stream.Communicator().Logger;
                    string slicingCat = _stream.Communicator().traceLevels().slicingCat;
                    if (_current.sliceType == SliceType.ExceptionSlice)
                    {
                        IceInternal.TraceUtil.traceSlicing("exception", _current.typeId, slicingCat, logger);
                    }
                    else
                    {
                        IceInternal.TraceUtil.traceSlicing("object", _current.typeId, slicingCat, logger);
                    }
                }

                int start = _stream.pos();

                if ((_current.sliceFlags & Protocol.FLAG_HAS_SLICE_SIZE) != 0)
                {
                    Debug.Assert(_current.sliceSize >= 4);
                    _stream.skip(_current.sliceSize - 4);
                }
                else
                {
                    if (_current.sliceType == SliceType.ClassSlice)
                    {
                        throw new NoClassFactoryException("no class factory found and compact format prevents " +
                                                          "slicing (the sender should use the sliced format " +
                                                          "instead)", _current.typeId);
                    }
                    else
                    {
                        if (_current.typeId.StartsWith("::", StringComparison.Ordinal))
                        {
                            throw new UnknownUserException(_current.typeId.Substring(2));
                        }
                        else
                        {
                            throw new UnknownUserException(_current.typeId);
                        }
                    }
                }

                //
                // Preserve this slice.
                //
                string typeId = _current.typeId;
                int compactId = _current.compactId;
                bool hasOptionalMembers = (_current.sliceFlags & Protocol.FLAG_HAS_OPTIONAL_MEMBERS) != 0;
                bool isLastSlice = (_current.sliceFlags & Protocol.FLAG_IS_LAST_SLICE) != 0;
                IceInternal.ByteBuffer b = _stream.GetBuffer().b;
                int end = b.position();
                int dataEnd = end;
                if (hasOptionalMembers)
                {
                    //
                    // Don't include the optional member end marker. It will be re-written by
                    // endSlice when the sliced data is re-written.
                    //
                    --dataEnd;
                }
                byte[] bytes = new byte[dataEnd - start];
                b.position(start);
                b.get(bytes);
                b.position(end);

                _current.slices ??= new List<SliceInfo>();
                var info = new SliceInfo(typeId, compactId, bytes, Array.Empty<AnyClass>(), hasOptionalMembers,
                                         isLastSlice);
                _current.slices.Add(info);

                // The deferred indirection table is only used by classes. For exceptions, the indirection table is
                // unmarshaled immediately.
                if (_current.sliceType == SliceType.ClassSlice)
                {
                    _current.DeferredIndirectionTableList ??= new List<int>();
                    if ((_current.sliceFlags & Protocol.FLAG_HAS_INDIRECTION_TABLE) != 0)
                    {
                        _current.DeferredIndirectionTableList.Add(_stream.pos());
                        SkipIndirectionTable();
                    }
                    else
                    {
                        _current.DeferredIndirectionTableList.Add(0);
                    }
                }
                else
                {
                    _current.IndirectionTableList ??= new List<AnyClass[]?>();
                    if ((_current.sliceFlags & Protocol.FLAG_HAS_INDIRECTION_TABLE) != 0)
                    {
                        Debug.Assert(_current.IndirectionTable != null); // previously read by startSlice
                        _current.IndirectionTableList.Add(_current.IndirectionTable);
                        _stream.pos(_current.PosAfterIndirectionTable.Value);
                        _current.PosAfterIndirectionTable = null;
                        _current.IndirectionTable = null;
                    }
                    else
                    {
                        _current.IndirectionTableList.Add(null);
                    }
                }
            }

            // Skip the indirection table. The caller must save the current stream position before calling
            // SkipIndirectionTable (to read the indirection table at a later point) except when the caller
            // is SkipIndirectionTable itself.
            private void SkipIndirectionTable()
            {
                // we should never skip an exception's indirection table
                Debug.Assert(_current.sliceType == SliceType.ClassSlice);

                // We use ReadSize and not ReadAndCheckSeqSize here because we don't allocate memory for this
                // sequence, and since we are skipping this sequence to read it later, we don't want to double-count
                // its contribution to _minTotalSeqSize.
                var tableSize = _stream.ReadSize();
                for (int i = 0; i < tableSize; ++i)
                {
                    var index = _stream.ReadSize();
                    if (index <= 0)
                    {
                        throw new MarshalException($"read invalid index {index} in indirection table");
                    }
                    if (index == 1)
                    {
                        if (++_classGraphDepth > _classGraphDepthMax)
                        {
                            throw new MarshalException("maximum class graph depth reached");
                        }

                        // Read/skip this instance
                        byte sliceFlags = 0;
                        do
                        {
                            sliceFlags = _stream.ReadByte();
                            if ((sliceFlags & Protocol.FLAG_HAS_TYPE_ID_COMPACT) == Protocol.FLAG_HAS_TYPE_ID_COMPACT)
                            {
                                _stream.ReadSize(); // compact type-id
                            }
                            else if ((sliceFlags &
                                (Protocol.FLAG_HAS_TYPE_ID_INDEX | Protocol.FLAG_HAS_TYPE_ID_STRING)) != 0)
                            {
                                // This can update the typeIdMap
                                readTypeId((sliceFlags & Protocol.FLAG_HAS_TYPE_ID_INDEX) != 0);
                            }
                            else
                            {
                                throw new MarshalException(
                                    "indirection table cannot hold an instance without a type-id");
                            }

                            // Read the slice size, then skip the slice

                            if ((sliceFlags & Protocol.FLAG_HAS_SLICE_SIZE) == 0)
                            {
                                throw new MarshalException("size of slice missing");
                            }
                            int sliceSize = _stream.ReadInt();
                            if (sliceSize < 4)
                            {
                                 throw new MarshalException("invalid slice size");
                            }
                            _stream.pos(_stream.pos() + sliceSize - 4);

                            // If this slice has an indirection table, skip it too
                            if ((sliceFlags & Protocol.FLAG_HAS_INDIRECTION_TABLE) != 0)
                            {
                                SkipIndirectionTable();
                            }
                        } while ((sliceFlags & Protocol.FLAG_IS_LAST_SLICE) == 0);
                        _classGraphDepth--;
                    }
                }
            }

            private AnyClass[] ReadIndirectionTable()
            {
                var size = _stream.ReadAndCheckSeqSize(1);
                if (size == 0)
                {
                    throw new MarshalException("invalid empty indirection table");
                }
                var indirectionTable = new AnyClass[size];
                for (int i = 0; i < indirectionTable.Length; ++i)
                {
                    indirectionTable[i] = readInstance(_stream.ReadSize());
                }
                return indirectionTable;
            }

            internal override bool readOptional(int readTag, OptionalFormat expectedFormat)
            {
                if (_current == null)
                {
                    return _stream.readOptImpl(readTag, expectedFormat);
                }
                else if ((_current.sliceFlags & Protocol.FLAG_HAS_OPTIONAL_MEMBERS) != 0)
                {
                    return _stream.readOptImpl(readTag, expectedFormat);
                }
                return false;
            }

            private void Unmarshal(int index, AnyClass v)
            {
                //
                // Add the instance to the map of unmarshaled instances, this must
                // be done before reading the instances (for circular references).
                //
                _unmarshaledMap.Add(index, v);

                //
                // Read all the deferred indirection tables now that the instance is inserted in _unmarshaledMap.
                //
                if (_current.DeferredIndirectionTableList?.Count > 0)
                {
                    int savedPos = _stream.pos();

                    Debug.Assert(_current.IndirectionTableList == null || _current.IndirectionTableList.Count == 0);
                    _current.IndirectionTableList ??= new List<AnyClass[]?>(_current.DeferredIndirectionTableList.Count);
                    foreach (int pos in _current.DeferredIndirectionTableList)
                    {
                        if (pos > 0)
                        {
                            _stream.pos(pos);
                            _current.IndirectionTableList.Add(ReadIndirectionTable());
                        }
                        else
                        {
                            _current.IndirectionTableList.Add(null);
                        }
                    }
                    _stream.pos(savedPos);
                    _current.DeferredIndirectionTableList.Clear();
                }

                //
                // Read the instance.
                //
                v.iceRead(_stream);
            }

            private AnyClass readInstance(int index)
            {
                Debug.Assert(index > 0);

                if (index > 1)
                {
                    if (_unmarshaledMap.TryGetValue(index, out var obj))
                    {
                        return obj;
                    }
                    throw new MarshalException($"could not find index {index} in unmarshaledMap");
                }

                push(SliceType.ClassSlice);

                //
                // Get the instance ID before we start reading slices. If some
                // slices are skipped, the indirect instance table are still read and
                // might read other instances.
                //
                index = ++_valueIdIndex;

                //
                // Read the first slice header.
                //
                startSlice(false);
                string mostDerivedId = _current.typeId;
                AnyClass? v = null;
                while (true)
                {
                    bool updateCache = false;

                    if (_current.compactId >= 0)
                    {
                        updateCache = true;

                        //
                        // Translate a compact (numeric) type ID into a class.
                        //
                        if (_compactIdCache == null)
                        {
                            _compactIdCache = new Dictionary<int, Type>(); // Lazy initialization.
                        }
                        else
                        {
                            //
                            // Check the cache to see if we've already translated the compact type ID into a class.
                            //
                            Type? cls = null;
                            _compactIdCache.TryGetValue(_current.compactId, out cls);
                            if (cls != null)
                            {
                                try
                                {
                                    Debug.Assert(!cls.IsAbstract && !cls.IsInterface);
                                    v = (AnyClass?)IceInternal.AssemblyUtil.createInstance(cls);
                                    updateCache = false;
                                }
                                catch (Exception ex)
                                {
                                    throw new NoClassFactoryException("no class factory", "compact ID " +
                                                                      _current.compactId, ex);
                                }
                            }
                        }

                        //
                        // If we haven't already cached a class for the compact ID, then try to translate the
                        // compact ID into a type ID.
                        //
                        if (v == null)
                        {
                            _current.typeId = "";
                            if (_compactIdResolver != null)
                            {
                                try
                                {
                                    _current.typeId = _compactIdResolver(_current.compactId);
                                }
                                catch (LocalException)
                                {
                                    throw;
                                }
                                catch (System.Exception ex)
                                {
                                    throw new MarshalException("exception in CompactIdResolver for ID " +
                                                                   _current.compactId, ex);
                                }
                            }

                            if (_current.typeId.Length == 0)
                            {
                                Communicator? communicator = _stream.Communicator();
                                Debug.Assert(communicator != null);
                                _current.typeId = communicator.resolveCompactId(_current.compactId);
                            }
                        }
                    }

                    if (v == null && _current.typeId.Length > 0)
                    {
                        v = newInstance(_current.typeId);
                    }

                    if (v != null)
                    {
                        if (updateCache)
                        {
                            Debug.Assert(_current.compactId >= 0);
                            _compactIdCache.Add(_current.compactId, v.GetType());
                        }

                        //
                        // We have an instance, get out of this loop.
                        //
                        break;
                    }

                    //
                    // If slicing is disabled, stop unmarshaling.
                    //
                    if (!_sliceClasses)
                    {
                        throw new NoClassFactoryException("no class factory found and slicing is disabled",
                                                          _current.typeId);
                    }

                    //
                    // Slice off what we don't understand.
                    //
                    skipSlice();

                    //
                    // If this is the last slice, keep the instance as an opaque
                    // UnknownSlicedClass object.
                    //
                    if ((_current.sliceFlags & Protocol.FLAG_IS_LAST_SLICE) != 0)
                    {
                        //
                        // Provide a factory with an opportunity to supply the instance.
                        // We pass the "::Ice::Object" ID to indicate that this is the
                        // last chance to preserve the instance.
                        //
                        v = newInstance(AnyClass.ice_staticId());
                        if (v == null)
                        {
                            v = new UnknownSlicedClass(mostDerivedId);
                        }

                        break;
                    }

                    startSlice(false); // Read next Slice header for next iteration.
                }

                if (++_classGraphDepth > _classGraphDepthMax)
                {
                    throw new MarshalException("maximum class graph depth reached");
                }

                //
                // Unmarshal the instance.
                //
                Unmarshal(index, v);

                --_classGraphDepth;

               return v;
            }

            private SlicedData? readSlicedData()
            {
                if (_current.slices == null) // No preserved slices.
                {
                    return null;
                }

                //
                // The IndirectionTableList member holds the indirection table for each slice
                // in _slices.
                //
                Debug.Assert(_current.slices.Count == _current.IndirectionTableList.Count);
                for (int n = 0; n < _current.slices.Count; ++n)
                {
                    //
                    // We use the "instances" list in SliceInfo to hold references
                    // to the target instances. Note that the instances might not have
                    // been read yet in the case of a circular reference to an
                    // enclosing instance.
                    //
                    SliceInfo info = _current.slices[n];
                    info.instances = _current.IndirectionTableList[n];
                }

                return new SlicedData(_current.slices.ToArray());
            }

            private void push(SliceType sliceType)
            {
                if (_current == null)
                {
                    _current = new InstanceData(null);
                }
                else
                {
                    _current = _current.next == null ? new InstanceData(_current) : _current.next;
                }
                _current.sliceType = sliceType;
                _current.skipFirstSlice = false;
            }
            private sealed class InstanceData
            {
                internal InstanceData(InstanceData? previous)
                {
                    if (previous != null)
                    {
                        previous.next = this;
                    }
                    this.previous = previous;
                    this.next = null;
                }

                // Instance attributes
                internal SliceType sliceType;
                internal bool skipFirstSlice;
                internal List<SliceInfo> slices;     // Preserved slices.
                internal List<AnyClass[]?>? IndirectionTableList;

                // Position of indirection tables that we skipped for now and that will
                // unmarshal (into IndirectionTableList) once the instance is created
                internal List<int>? DeferredIndirectionTableList;

                // Slice attributes
                internal byte sliceFlags;
                internal int sliceSize;
                internal string typeId;
                internal int compactId;

                // Indirection table of the current slice
                internal AnyClass[]? IndirectionTable;
                internal int? PosAfterIndirectionTable;

                // Other instances
                internal InstanceData? previous;
                internal InstanceData? next;
            }

            private Func<int, string> _compactIdResolver;
            private InstanceData _current;
            private int _valueIdIndex; // The ID of the next instance to unmarshal.
            private Dictionary<int, Type> _compactIdCache;
        }

        private sealed class Encaps
        {
            internal void reset()
            {
                decoder = null;
            }

            internal void setEncoding(EncodingVersion encoding)
            {
                this.encoding = encoding;
                encoding_1_0 = encoding.Equals(Util.Encoding_1_0);
            }

            internal int start;
            internal int sz;
            internal EncodingVersion encoding;
            internal bool encoding_1_0;

            internal EncapsDecoder decoder;

            internal Encaps next;
        }

        //
        // The encoding version to use when there's no encapsulation to
        // read from. This is for example used to read message headers.
        //
        private EncodingVersion _encoding;

        private bool isEncoding_1_0()
        {
            return _encapsStack != null ? _encapsStack.encoding_1_0 : _encoding.Equals(Util.Encoding_1_0);
        }

        private Encaps? _encapsStack;
        private Encaps _encapsCache;

        private void initEncaps()
        {
            if (_encapsStack == null) // Lazy initialization
            {
                _encapsStack = _encapsCache;
                if (_encapsStack != null)
                {
                    _encapsCache = _encapsCache.next;
                }
                else
                {
                    _encapsStack = new Encaps();
                }
                _encapsStack.setEncoding(_encoding);
                _encapsStack.sz = _buf.b.limit();
            }

            if (_encapsStack.decoder == null) // Lazy initialization.
            {
                if (_encapsStack.encoding_1_0)
                {
                    // TODO: temporary until larger refactoring
                    Debug.Assert(false);
                }
                else
                {
                    _encapsStack.decoder = new EncapsDecoder11(this, _encapsStack, _sliceClasses, _classGraphDepthMax,
                                                               _classResolver, _compactIdResolver);
                }
            }
        }

        private bool _sliceClasses;
        private bool _traceSlicing;
        private int _classGraphDepthMax;

        // The sum of all the mininum sizes (in bytes) of the sequences
        // read in this buffer. Must not exceed the buffer size.
        private int _minTotalSeqSize = 0;

        private ILogger _logger;
        private Func<int, string> _compactIdResolver;
        private Func<string, Type?> _classResolver;
    }

    /// <summary>
    /// Base class for extracting class instances from an input stream.
    /// </summary>
    public abstract class ClassReader : AnyClass
    {
        /// <summary>
        /// Read the instance's data members.
        /// </summary>
        /// <param name="inStream">The input stream to read from.</param>
        public abstract void read(InputStream inStream);

        public override void iceWrite(OutputStream os)
        {
            Debug.Assert(false);
        }

        public override void iceRead(InputStream istr)
        {
            read(istr);
        }
    }
}
