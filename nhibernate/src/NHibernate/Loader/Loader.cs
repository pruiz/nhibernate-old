using System;
using System.Collections;
using System.Data;
using log4net;
using NHibernate.Collection;
using NHibernate.Engine;
using NHibernate.Persister;
using NHibernate.SqlCommand;
using NHibernate.Type;
using NHibernate.Util;

namespace NHibernate.Loader
{
	/// <summary>
	/// Abstract superclass of object loading (and querying) strategies.
	/// </summary>
	/// <remarks>
	/// This class implements useful common funtionality that concrete loaders would delegate to.
	/// It is not intended that this functionality would be directly accessed by client code (Hence,
	/// all methods of this class are declared <c>protected</c>.)
	/// </remarks>
	public abstract class Loader
	{
		private static readonly ILog log = LogManager.GetLogger( typeof( Loader ) );
		private Dialect.Dialect dialect;

		/// <summary>
		/// 
		/// </summary>
		/// <param name="dialect"></param>
		protected Loader( Dialect.Dialect dialect )
		{
			this.dialect = dialect;
		}

		/// <summary>
		/// Gets a reference to the <see cref="Dialect"/> to use
		/// when building the <see cref="SqlString"/>.
		/// </summary>
		protected internal Dialect.Dialect Dialect
		{
			get { return dialect; }
		}

		/// <summary>
		/// The SqlString to be called; implemented by all subclasses
		/// </summary>
		/// <remarks>
		/// <p>
		/// The <c>setter</c> was added so that class inheriting from Loader could write a 
		/// value using the Property instead of directly to the field.
		/// </p>
		/// <p>
		/// The scope is <c>internal</c> because the <see cref="Hql.WhereParser"/> needs to
		/// be able to <c>get</c> the SqlString of the <see cref="Hql.QueryTranslator"/> when
		/// it is parsing a subquery.
		/// </p>
		/// </remarks>
		protected internal abstract SqlString SqlString { get; set; }

		/// <summary>
		/// An array of persisters of entity classes contained in each row of results;
		/// implemented by all subclasses
		/// </summary>
		/// <remarks>
		/// <p>
		/// The <c>setter</c> was added so that classes inheriting from Loader could write a 
		/// value using the Property instead of directly to the field.
		/// </p>
		/// </remarks>
		protected abstract ILoadable[ ] Persisters { get; set; }

		/// <summary>
		/// The suffix identifies a particular column of results in the SQL <c>IDataReader</c>;
		/// implemented by all subclasses
		/// </summary>
		/// added set
		protected abstract string[ ] Suffixes { get; set; }

		/// <summary>
		/// An (optional) persister for a collection to be initialized; only collection loaders
		/// return a non-null value
		/// </summary>
		protected abstract CollectionPersister CollectionPersister { get; }

		/// <summary>
		/// It should be overridden in Hql.QueryTranslator and an actual value placed in there.
		/// </summary>
		protected virtual int CollectionOwner
		{
			get { return -1; }
		}


		/// <summary>
		/// What lock mode does this load entities with?
		/// </summary>
		/// <param name="lockModes">A Collection of lock modes specified dynamically via the Query Interface</param>
		/// <returns></returns>
		protected abstract LockMode[ ] GetLockModes( IDictionary lockModes );


		/// <summary>
		/// Append <c>FOR UPDATE OF</c> clause, if necessary
		/// </summary>
		/// <param name="sql"></param>
		/// <param name="lockModes"></param>
		/// <param name="dialect"></param>
		/// <returns></returns>
		protected virtual SqlString ApplyLocks( SqlString sql, IDictionary lockModes, Dialect.Dialect dialect )
		{
			return sql;
		}

		/// <summary>
		/// Does this Query return objects that might be already cached by 
		/// the session, whose lock mode may need upgrading.
		/// </summary>
		/// <returns></returns>
		protected virtual bool UpgradeLocks()
		{
			return false;
		}

		// This method is called DoQueryAndInitializeNonLazyCollections in H2.1,
		// since DoFind is called DoQuery and is split into several smaller methods.
		private IList DoFindAndInitializeNonLazyCollections(
			ISessionImplementor session,
			QueryParameters parameters,
			object optionalObject,
			object optionalID,
			PersistentCollection optionalCollection,
			object optionalCollectionOwner,
			bool returnProxies )
		{
			session.BeforeLoad();

			IList result;

			try
			{
				result = DoFind(
					session, parameters, optionalObject, optionalID,
					optionalCollection, optionalCollectionOwner, returnProxies);
			}
			finally
			{
				session.AfterLoad();
			}

			session.InitializeNonLazyCollections();
			return result;
		}

		/// <summary>
		/// Execute an SQL query and attempt to instantiate instances of the class mapped by the given
		/// persister from each row of the <c>IDataReader</c>.
		/// </summary>
		/// <remarks>
		/// If an object is supplied, will attempt to initialize that object. if a collection is supplied,
		/// attemp to initialize that collection
		/// </remarks>
		/// <param name="session"></param>
		/// <param name="parameters"></param>
		/// <param name="optionalObject"></param>
		/// <param name="optionalID"></param>
		/// <param name="optionalCollection"></param>
		/// <param name="optionalCollectionOwner"></param>
		/// <param name="returnProxies"></param>
		/// <returns></returns>
		private IList DoFind(
			ISessionImplementor session,
			QueryParameters parameters,
			object optionalObject,
			object optionalID,
			PersistentCollection optionalCollection,
			object optionalCollectionOwner,
			bool returnProxies )
		{
			int maxRows = ( parameters.RowSelection == null || parameters.RowSelection.MaxRows == RowSelection.NoValue ) ?
				int.MaxValue : parameters.RowSelection.MaxRows;

			ILoadable[ ] persisters = Persisters;
			int cols = persisters.Length;
			CollectionPersister collectionPersister = this.CollectionPersister;
			int collectionOwner = this.CollectionOwner;
			bool returnsEntities = cols > 0;
			string[ ] suffixes = Suffixes;

			LockMode[ ] lockModeArray = GetLockModes( parameters.LockModes );

			// this is a CollectionInitializer and we are loading up a single collection
			bool singleCollection = collectionPersister != null && optionalCollection != null;

			// this is a Query and we are loading multiple instances of the same collection role
			bool multipleCollections = collectionPersister != null && optionalCollection == null && CollectionOwner >= 0;

			ArrayList hydratedObjects = returnsEntities ? new ArrayList() : null;

			Key optionalObjectKey;
			if( optionalObject != null )
			{
				optionalObjectKey = new Key( optionalID, session.GetPersister( optionalObject ) );
			}
			else
			{
				optionalObjectKey = null;
			}

			IList results = new ArrayList();

			IDbCommand st = null;

			st = PrepareCommand(
				ApplyLocks( SqlString, parameters.LockModes, session.Factory.Dialect ),
				parameters,
				false,
				session );

			IDataReader rs = GetResultSet( st, parameters.RowSelection, session );

			try
			{
				if( singleCollection )
				{
					optionalCollection.BeginRead();
				}

				Key[ ] keys = new Key[cols];

				if( log.IsDebugEnabled )
				{
					log.Debug( "processing result set" );
				}

				int count;
				for( count = 0; count < maxRows && rs.Read(); count++ )
				{
					for( int i = 0; i < cols; i++ )
					{
						keys[ i ] = GetKeyFromResultSet( i, persisters[ i ], ( i == cols - 1 ) ? optionalID : null, rs, session );
						//TODO: the i==cols-1 bit depends upon subclass implementation (very bad)
					}

					//this call is side-effecty 
					object[ ] row = GetRow( rs, persisters, suffixes, keys, optionalObject, optionalObjectKey, lockModeArray, hydratedObjects, session );

					if( returnProxies )
					{
						for( int i = 0; i < cols; i++ )
						{
							row[ i ] = session.ProxyFor( persisters[ i ], keys[ i ], row[ i ] );
						}
					}

					if( multipleCollections )
					{
						Key ownerKey = keys[ collectionOwner ];
						if( ownerKey != null )
						{
							PersistentCollection rowCollection = session.GetLoadingCollection( collectionPersister, ownerKey.Identifier );
							object collectionRowKey = collectionPersister.ReadKey( rs, session );
							if( collectionRowKey != null )
							{
								rowCollection.ReadFrom( rs, CollectionPersister, row[ collectionOwner ] );
							}
						}
					}
					else if( singleCollection )
					{
						optionalCollection.ReadFrom( rs, CollectionPersister, optionalCollectionOwner );
					}

					results.Add( GetResultColumnOrRow( row, rs, session ) );

					if( log.IsDebugEnabled )
					{
						log.Debug( "done processing result set(" + count + " rows)" );
					}

				}
			}
				// TODO: change to SqlException
			catch( Exception )
			{
				// TODO: log the SqlException
				throw;
			}
			finally
			{
				try
				{
					rs.Close();
				}
				finally
				{
					ClosePreparedCommand( st, rs, session );
				}
			}

			if( returnsEntities )
			{
				int hydratedObjectsSize = hydratedObjects.Count;
				if( log.IsDebugEnabled )
				{
					log.Debug( "total objects hydrated: " + hydratedObjectsSize );
				}
				for( int i = 0; i < hydratedObjectsSize; i++ )
				{
					session.InitializeEntity( hydratedObjects[ i ] );
				}
			}

			if( multipleCollections )
			{
				session.EndLoadingCollections();
			}
			if( singleCollection )
			{
				optionalCollection.EndRead();
			}


			return results;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="row"></param>
		/// <param name="rs"></param>
		/// <param name="session"></param>
		/// <returns></returns>
		protected virtual object GetResultColumnOrRow( object[ ] row, IDataReader rs, ISessionImplementor session )
		{
			return row;
		}

		/// <summary>
		/// Read a row of <c>Key</c>s from the <c>IDataReader</c> into the given array.
		/// </summary>
		/// <remarks>
		/// Warning: this method is side-effecty. If an <c>id</c> is given, don't bother going
		/// to the <c>IDataReader</c>
		/// </remarks>
		/// <param name="persister"></param>
		/// <param name="id"></param>
		/// <param name="rs"></param>
		/// <param name="session"></param>
		/// <param name="i"></param>
		/// <returns></returns>
		private Key GetKeyFromResultSet( int i, ILoadable persister, object id, IDataReader rs, ISessionImplementor session )
		{
			if( id == null )
			{
				id = persister.IdentifierType.NullSafeGet( rs, suffixedKeyColumns[ i ], session, null );
			}

			return ( id == null ) ? null : new Key( id, persister );
		}

		/// <summary>
		/// Check the version of the object in the <c>IDataReader</c> against
		/// the object version in the session cache, throwing an exception
		/// if the vesrion numbers are different.
		/// </summary>
		/// <param name="i"></param>
		/// <param name="persister"></param>
		/// <param name="suffix"></param>
		/// <param name="id"></param>
		/// <param name="version"></param>
		/// <param name="dr"></param>
		/// <param name="session"></param>
		/// <exception cref="StaleObjectStateException"></exception>
		private void CheckVersion( int i, ILoadable persister, string suffix, object id, object version, IDataReader dr, ISessionImplementor session )
		{
			// null version means the object is in the process of being loaded somewhere
			// else in the ResultSet
			if( version != null )
			{
				IType versionType = persister.VersionType;
				object currentVersion = versionType.NullSafeGet( dr, suffixedVersionColumnNames[ i ], session, null );
				if( !versionType.Equals( version, currentVersion ) )
				{
					throw new StaleObjectStateException( persister.MappedClass, id );
				}

			}
		}

		/// <summary>
		/// Resolve any ids for currently loaded objects, duplications within the <c>IDataReader</c>,
		/// etc. Instanciate empty objects to be initialized from the <c>IDataReader</c>. Return an
		/// array of objects (a row of results) and an array of booleans (by side-effect) that determine
		/// wheter the corresponding object should be initialized
		/// </summary>
		/// <param name="rs"></param>
		/// <param name="persisters"></param>
		/// <param name="suffixes"></param>
		/// <param name="keys"></param>
		/// <param name="optionalObject"></param>
		/// <param name="optionalObjectKey"></param>
		/// <param name="session"></param>
		/// <param name="hydratedObjects"></param>
		/// <param name="lockModes"></param>
		/// <returns></returns>
		private object[ ] GetRow(
			IDataReader rs,
			ILoadable[ ] persisters,
			string[ ] suffixes,
			Key[ ] keys,
			object optionalObject,
			Key optionalObjectKey,
			LockMode[ ] lockModes,
			IList hydratedObjects,
			ISessionImplementor session )
		{
			int cols = persisters.Length;

			if( log.IsDebugEnabled )
			{
				log.Debug( "result row: " + StringHelper.ToString( keys ) );
			}

			object[ ] rowResults = new object[cols];

			for( int i = 0; i < cols; i++ )
			{
				object obj = null;
				Key key = keys[ i ];

				if( keys[ i ] == null )
				{
					// do nothing - used to have hydrate[i] = false;
				}
				else
				{
					//If the object is already loaded, return the loaded one
					obj = session.GetEntity( key );
					if( obj != null )
					{
						//its already loaded so dont need to hydrate it
						InstanceAlreadyLoaded( rs, i, persisters[ i ], suffixes[ i ], key, obj, lockModes[ i ], session );
					}
					else
					{
						obj = InstanceNotYetLoaded( rs, i, persisters[ i ], suffixes[ i ], key, lockModes[ i ], optionalObjectKey, optionalObject, hydratedObjects, session );
					}
				}

				rowResults[ i ] = obj;
			}
			return rowResults;
		}

		private void InstanceAlreadyLoaded( IDataReader dr, int i, ILoadable persister, string suffix, Key key, object obj, LockMode lockMode, ISessionImplementor session )
		{
			if( !persister.MappedClass.IsAssignableFrom( obj.GetType() ) )
			{
				throw new WrongClassException( "loading object was of wrong class", key.Identifier, persister.MappedClass );
			}

			if( LockMode.None != lockMode && UpgradeLocks() )
			{
				// we don't need to worry about existing version being uninitialized
				// because this block isn't called by a re-entrant load (re-entrant
				// load _always_ have lock mode NONE
				if( persister.IsVersioned && session.GetLockMode( obj ).LessThan( lockMode ) )
				{
					// we only check the version when _upgrading_ lock modes
					CheckVersion( i, persister, suffix, key.Identifier, session.GetVersion( obj ), dr, session );
					// we need to upgrade the lock mode to the mode requested
					session.SetLockMode( obj, lockMode );
				}
			}
		}

		private object InstanceNotYetLoaded( IDataReader dr, int i, ILoadable persister, string suffix, Key key, LockMode lockMode, Key optionalObjectKey, object optionalObject, IList hydratedObjects, ISessionImplementor session )
		{
			object obj;

			System.Type instanceClass = GetInstanceClass( dr, i, persister, suffix, key.Identifier, session );

			if( optionalObjectKey != null && key.Equals( optionalObjectKey ) )
			{
				// its the given optional object
				obj = optionalObject;
			}
			else
			{
				obj = session.Instantiate( instanceClass, key.Identifier );
			}

			// need to hydrate it

			// grab its state from the DataReader and keep it in the Session
			// (but don't yet initialize the object itself)
			// note that we acquired LockMode.READ even if it was not requested
			LockMode acquiredLockMode = lockMode == LockMode.None ? LockMode.Read : lockMode;
			LoadFromResultSet( dr, i, obj, key, suffix, acquiredLockMode, persister, session );

			// materialize associations (and initialize the object) later
			hydratedObjects.Add( obj );

			return obj;
		}

		/// <summary>
		/// Hydrate an object from the SQL <c>IDataReader</c>
		/// </summary>
		/// <param name="rs"></param>
		/// <param name="i"></param>
		/// <param name="obj"></param>
		/// <param name="key"></param>
		/// <param name="suffix"></param>
		/// <param name="lockMode"></param>
		/// <param name="rootPersister"></param>
		/// <param name="session"></param>
		private void LoadFromResultSet( IDataReader rs, int i, object obj, Key key, string suffix, LockMode lockMode, ILoadable rootPersister, ISessionImplementor session )
		{
			if( log.IsDebugEnabled )
			{
				log.Debug( "Initializing object from DataReader: " + key );
			}

			// add temp entry so that the next step is circular-reference
			// safe - only needed because some types don't take proper
			// advantage of two-phase-load (esp. components)
			session.AddUninitializedEntity( key, obj, lockMode );

			// Get the persister for the subclass
			ILoadable persister = ( ILoadable ) session.GetPersister( obj );

			string[ ][ ] cols = persister == rootPersister ?
				suffixedPropertyColumns[ i ] :
				GetPropertyAliases( suffix, persister );

			object id = key.Identifier;

			object[ ] values = Hydrate( rs, id, obj, persister, session, cols );
			session.PostHydrate( persister, id, values, obj, lockMode );
		}

		/// <summary>
		/// Determine the concrete class of an instance for the <c>IDataReader</c>
		/// </summary>
		/// <param name="rs"></param>
		/// <param name="i"></param>
		/// <param name="persister"></param>
		/// <param name="suffix"></param>
		/// <param name="id"></param>
		/// <param name="session"></param>
		/// <returns></returns>
		private System.Type GetInstanceClass( IDataReader rs, int i, ILoadable persister, string suffix, object id, ISessionImplementor session )
		{
			System.Type topClass = persister.MappedClass;

			if( persister.HasSubclasses )
			{
				// code to handle subclasses of topClass
				object discriminatorValue = persister.DiscriminatorType.NullSafeGet( rs, suffixedDiscriminatorColumn[ i ], session, null );

				System.Type result = persister.GetSubclassForDiscriminatorValue( discriminatorValue );

				if( result == null )
				{
					throw new WrongClassException( "Discriminator: " + discriminatorValue, id, topClass );
				}
				// woops we got an instance of another class heirarchy branch.

				return result;
			}
			else
			{
				return topClass;
			}
		}

		/// <summary>
		/// Unmarshall the fields of a persistent instance from a result set
		/// </summary>
		/// <param name="rs"></param>
		/// <param name="id"></param>
		/// <param name="obj"></param>
		/// <param name="persister"></param>
		/// <param name="session"></param>
		/// <param name="suffixedPropertyColumns"></param>
		/// <returns></returns>
		private object[ ] Hydrate( IDataReader rs, object id, object obj, ILoadable persister, ISessionImplementor session, string[ ][ ] suffixedPropertyColumns )
		{
			if( log.IsDebugEnabled )
			{
				log.Debug( "Hydrating entity: " + persister.ClassName + '#' + id );
			}

			IType[ ] types = persister.PropertyTypes;
			object[ ] values = new object[types.Length];

			for( int i = 0; i < types.Length; i++ )
			{
				values[ i ] = types[ i ].Hydrate( rs, suffixedPropertyColumns[ i ], session, obj );
			}
			return values;
		}

		/// <summary>
		/// Advance the cursor to the first required row of the <c>ResultSet</c>
		/// </summary>
		/// <param name="rs"></param>
		/// <param name="selection"></param>
		/// <param name="session"></param>
		protected void Advance( IDataReader rs, RowSelection selection, ISessionImplementor session )
		{
			int firstRow = Loader.GetFirstRow( selection );

			if( firstRow != 0 )
			{
				for( int i = 0; i < firstRow; i++ )
				{
					rs.Read();
				}
			}

		}

		private static int GetFirstRow( RowSelection selection )
		{
			if( selection == null )
				// || selection.FirstRow==null -> won't ever be null because structs are initialized... 
			{
				return 0;
			}
			else
			{
				return selection.FirstRow;
			}
		}

		private static bool UseLimit( RowSelection selection, Dialect.Dialect dialect )
		{
			// it used to be selection.MaxRows != null -> since an Int32 will always
			// have a value I'll compare it to the static field NoValue used to initialize 
			// max rows to nothing
			return dialect.SupportsLimit &&
				( selection != null && selection.MaxRows != RowSelection.NoValue ) && // there is a max rows
				( dialect.PreferLimit || GetFirstRow( selection ) != 0 );
		}

		/// <summary>
		/// Creates an IDbCommand object and populates it with the values necessary to execute it against the 
		/// database to Load an Entity.
		/// </summary>
		/// <param name="sqlString">The SqlString to convert into a prepared IDbCommand.</param>
		/// <param name="parameters">The <see cref="QueryParameters"/> to use for the IDbCommand.</param>
		/// <param name="scroll">TODO: find out where this is used...</param>
		/// <param name="session">The SessionImpl this Command is being prepared in.</param>
		/// <returns>An IDbCommand that is ready to be executed.</returns>
		protected virtual IDbCommand PrepareCommand( SqlString sqlString, QueryParameters parameters, bool scroll, ISessionImplementor session )
		{
			Dialect.Dialect dialect = session.Factory.Dialect;

			bool useLimit = UseLimit( parameters.RowSelection, dialect );
			bool scrollable = scroll || ( !useLimit && GetFirstRow( parameters.RowSelection ) != 0 );
			if( useLimit )
			{
				sqlString = dialect.GetLimitString( sqlString );
			}

			IDbCommand command = session.Batcher.PrepareQueryCommand( sqlString, scrollable );

			try
			{
				if( parameters.RowSelection != null && parameters.RowSelection.Timeout != RowSelection.NoValue )
				{
					command.CommandTimeout = parameters.RowSelection.Timeout;
				}

				int colIndex = 0;

				if( useLimit && dialect.BindLimitParametersFirst )
				{
					BindLimitParameters( command, colIndex, parameters.RowSelection, session );
					colIndex += 2;
				}

				for( int i = 0; i < parameters.PositionalParameterValues.Length; i++ )
				{
					parameters.PositionalParameterTypes[ i ].NullSafeSet( command, parameters.PositionalParameterValues[ i ], colIndex, session );
					colIndex += parameters.PositionalParameterTypes[ i ].GetColumnSpan( session.Factory );
				}

				//if (namedParams!=null)	
				colIndex += BindNamedParameters( command, parameters.NamedParameters, colIndex, session );

				if( useLimit && !dialect.BindLimitParametersFirst )
				{
					BindLimitParameters( command, colIndex, parameters.RowSelection, session );
				}

				if( !useLimit )
				{
					SetMaxRows( command, parameters.RowSelection );
				}
				if( parameters.RowSelection != null && parameters.RowSelection.Timeout != RowSelection.NoValue )
				{
					command.CommandTimeout = parameters.RowSelection.Timeout;
				}

			}
				//TODO: fix up the Exception handling here...
			catch( Exception )
			{
				ClosePreparedCommand( command, null, session );
				throw;
			}

			return command;
		}

		private void BindLimitParameters( IDbCommand st, int index, RowSelection selection, ISessionImplementor session )
		{
			int firstRow = ( selection == null ) ? 0 : selection.FirstRow;
			int lastRow = selection.MaxRows;
			Dialect.Dialect dialect = session.Factory.Dialect;

			if( dialect.UseMaxForLimit )
			{
				lastRow += firstRow;
			}
			bool reverse = dialect.BindLimitParametersInReverseOrder;

			( ( IDataParameter ) st.Parameters[ index + ( reverse ? 1 : 0 ) ] ).Value = firstRow;
			( ( IDataParameter ) st.Parameters[ index + ( reverse ? 0 : 1 ) ] ).Value = lastRow;
		}

		/// <summary>
		/// Limits the number of rows returned by the Sql query if necessary.
		/// </summary>
		/// <param name="st">The IDbCommand to limit.</param>
		/// <param name="selection">The RowSelection that contains the MaxResults info.</param>
		/// <remarks>TODO: This does not apply to ADO.NET at all</remarks>
		protected void SetMaxRows( IDbCommand st, RowSelection selection )
		{
			if( selection != null && selection.MaxRows != RowSelection.NoValue )
			{
				//TODO: H2.0.3 - do we need this method??
				// there is nothing in ADO.NET to do anything  similar
				// to Java's PreparedStatement.setMaxRows(int)
			}
		}

		/// <summary>
		/// Fetch a <c>IDbCommand</c>, call <c>SetMaxRows</c> and then execute it,
		/// advance to the first result and return an SQL <c>IDataReader</c>
		/// </summary>
		/// <param name="st">The <see cref="IDbCommand" /> to execute.</param>
		/// <param name="selection">The <see cref="RowSelection"/> to apply to the <see cref="IDbCommand"/> and <see cref="IDataReader"/>.</param>
		/// <param name="session">The <see cref="ISession" /> to load in.</param>
		/// <returns>An IDataReader advanced to the first record in RowSelection.</returns>
		protected IDataReader GetResultSet( IDbCommand st, RowSelection selection, ISessionImplementor session )
		{
			IDataReader rs = null;
			try
			{
				log.Info( st.CommandText );
				rs = session.Batcher.ExecuteReader( st );

				if( !UseLimit( selection, session.Factory.Dialect ) )
				{
					Advance( rs, selection, session );
				}

				return rs;
			}
			catch( Exception )
			{
				ClosePreparedCommand( st, rs, session );
				throw;
			}
		}


		/// <summary>
		/// Cleans up the resources used by this Loader.
		/// </summary>
		/// <param name="st">The <see cref="IDbCommand"/> to close.</param>
		/// <param name="reader">The <see cref="IDataReader"/> to close.</param>
		/// <param name="session">The <see cref="ISession"/> this Loader is using.</param>
		protected void ClosePreparedCommand( IDbCommand st, IDataReader reader, ISessionImplementor session )
		{
			session.Batcher.CloseQueryCommand( st, reader );
		}

		/// <summary>
		/// Bind named parameters to the <c>IDbCommand</c>
		/// </summary>
		/// <param name="st">The <see cref="IDbCommand"/> that contains the parameters.</param>
		/// <param name="namedParams">The named parameters (key) and the values to set.</param>
		/// <param name="session">The <see cref="ISession"/> this Loader is using.</param>
		/// <param name="start"></param>
		/// <remarks>
		/// This has an empty implementation on this superclass and should be implemented by
		/// sublcasses (queries) which allow named parameters.
		/// </remarks>
		protected virtual int BindNamedParameters( IDbCommand st, IDictionary namedParams, int start, ISessionImplementor session )
		{
			return 0;
		}


		/// <summary>
		/// Called by subclasses that load entities.
		/// </summary>
		/// <param name="session"></param>
		/// <param name="values"></param>
		/// <param name="types"></param>
		/// <param name="optionalObject"></param>
		/// <param name="optionalID"></param>
		/// <param name="returnProxies"></param>
		/// <returns></returns>
		protected IList LoadEntity(
			ISessionImplementor session,
			object[ ] values,
			IType[ ] types,
			object optionalObject,
			object optionalID,
			bool returnProxies )
		{
			QueryParameters qp = new QueryParameters( types, values );
			return DoFindAndInitializeNonLazyCollections( session, qp, optionalObject, optionalID, null, null, returnProxies );
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="session"></param>
		/// <param name="id"></param>
		/// <param name="type"></param>
		/// <param name="owner"></param>
		/// <param name="collection"></param>
		/// <returns></returns>
		protected IList LoadCollection(
			ISessionImplementor session,
			object id,
			IType type,
			object owner,
			PersistentCollection collection )
		{
			QueryParameters qp = new QueryParameters( new IType[ ] {type}, new object[ ] {id} );
			return DoFindAndInitializeNonLazyCollections( session, qp, null, null, collection, owner, true );
		}

		/// <summary>
		/// Called by subclasses that implement queries.
		/// </summary>
		/// <param name="session"></param>
		/// <param name="parameters"></param>
		/// <param name="returnProxies"></param>
		/// <returns></returns>
		protected virtual IList Find(
			ISessionImplementor session,
			QueryParameters parameters,
			bool returnProxies )
		{
			return DoFindAndInitializeNonLazyCollections( session, parameters, null, null, null, null, returnProxies );
		}

		private string[ ][ ] suffixedKeyColumns;
		private string[ ][ ] suffixedVersionColumnNames;
		private string[ ][ ][ ] suffixedPropertyColumns;
		private string[ ] suffixedDiscriminatorColumn;

		/// <summary></summary>
		protected void PostInstantiate()
		{
			ILoadable[ ] persisters = Persisters;
			string[ ] suffixes = Suffixes;
			suffixedKeyColumns = new string[persisters.Length][ ];
			suffixedPropertyColumns = new string[persisters.Length][ ][ ];
			suffixedVersionColumnNames = new string[persisters.Length][ ];
			suffixedDiscriminatorColumn = new string[persisters.Length];

			for( int i = 0; i < persisters.Length; i++ )
			{
				suffixedKeyColumns[ i ] = GetKeyAliases( suffixes[ i ], persisters[ i ] );
				suffixedPropertyColumns[ i ] = GetPropertyAliases( suffixes[ i ], persisters[ i ] );
				suffixedDiscriminatorColumn[ i ] = GetDiscriminatorAliases( suffixes[ i ], persisters[ i ] );
				if( persisters[ i ].IsVersioned )
				{
					suffixedVersionColumnNames[ i ] = suffixedPropertyColumns[ i ][ persisters[ i ].VersionProperty ];
				}
			}
		}

		private string[ ] GetKeyAliases( string suffix, ILoadable persister )
		{
			return new Alias( suffix ).ToUnquotedAliasStrings( persister.IdentifierColumnNames, dialect );
		}

		private string[ ][ ] GetPropertyAliases( string suffix, ILoadable persister )
		{
			int size = persister.PropertyNames.Length;
			string[ ][ ] result = new string[size][ ];
			for( int i = 0; i < size; i++ )
			{
				result[ i ] = new Alias( suffix ).ToUnquotedAliasStrings( persister.GetPropertyColumnNames( i ), dialect );
			}
			return result;
		}

		private string GetDiscriminatorAliases( string suffix, ILoadable persister )
		{
			return persister.HasSubclasses ?
				new Alias( suffix ).ToUnquotedAliasString( persister.DiscriminatorColumnName, dialect ) : null;
		}


	}
}