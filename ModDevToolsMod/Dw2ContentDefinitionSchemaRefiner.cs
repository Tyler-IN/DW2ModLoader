using System.Collections;
using DistantWorlds2.ModLoader;
using Json.Schema;
using Json.Schema.Generation;
using Json.Schema.Generation.Intents;

namespace ModDevToolsMod;

public class Dw2ContentDefinitionSchemaRefiner : ISchemaRefiner {

  private static readonly RefIntent ExprLangRefIntent = new(Mod.ExprLangRefUri);

  public Dw2ContentDefinitionSchemaRefiner(Type rootType)
    => RootType = rootType;

  public Type RootType { get; }

  private bool _rootTypeInit = false;

  public Dictionary<string, SchemaGeneratorContext> Definitions = new();

  public bool ShouldRun(SchemaGeneratorContext context)
    => !context.Type.IsPrimitive && context.Type != typeof(string)
      && context.Type.GetInterfaces().All(f => f != typeof(IEnumerable))
      && !context.Type.IsEnum;

  public void Run(SchemaGeneratorContext context) {
    var type = context.Type;

    if (type == typeof(ExplicitExpression)) {
      context.Intents.Clear();
      context.Intents.Add(ExprLangRefIntent);
      return;
    }

    if (type == typeof(NumberExpression)) {
      context.Intents.Clear();
      context.Intents.Add(new OneOfIntent(
        new ISchemaKeywordIntent[] { ExprLangRefIntent },
        new ISchemaKeywordIntent[] { new TypeIntent(SchemaValueType.Number) }
      ));
      return;
    }

    if (type == typeof(IntegerExpression)) {
      context.Intents.Clear();
      context.Intents.Add(new OneOfIntent(
        new ISchemaKeywordIntent[] { ExprLangRefIntent },
        new ISchemaKeywordIntent[] { new TypeIntent(SchemaValueType.Integer) }
      ));
      return;
    }

    if (type.IsGenericType) {
      if (type.GetGenericTypeDefinition() == typeof(AddToList<>)) {
        context.Intents.Clear();
        return;
      }

      if (type.GetGenericTypeDefinition() == typeof(Def<>)) {
        context.Intents.Clear();
        return;
      }

      if (type.GetGenericTypeDefinition() == typeof(ItemOrDelete<>)) {
        context.Intents.Clear();
        return;
      }
    }

    if (!_rootTypeInit)
      SchemaGenerationContextCache.Get(RootType, new(0), context.Configuration);

    var props = context.Intents.OfType<PropertiesIntent>().FirstOrDefault();
    if (props is null) return;

    var isDefType = Mod.DefTypes.Contains(context.Type);
    if (isDefType)
      if (context.Type != RootType) {
        context.Intents.Clear();
        context.Intents.Add(new RefIntent(new($"./def-{context.Type.Name}.json#", UriKind.Relative)));
        return;
      }

    var propsCopy = new Dictionary<string, SchemaGeneratorContext>(props.Properties);
#if DEBUG
    var minProps = props.Properties.Count;
#endif
    props.Properties.Clear();

    var sgcExplicitExpression = SchemaGenerationContextCache.Get(typeof(ExplicitExpression), new(0), context.Configuration);
    var sgcIntegerExpression = SchemaGenerationContextCache.Get(typeof(IntegerExpression), new(0), context.Configuration);
    var sgcNumberExpression = SchemaGenerationContextCache.Get(typeof(NumberExpression), new(0), context.Configuration);

    foreach (var kv in propsCopy) {
      var name = kv.Key;
      var propCtx = kv.Value;
      if (propCtx.Type == typeof(string)) {
        props.Properties.Add(name, propCtx);
        props.Properties.Add($"${name}", sgcExplicitExpression);
        continue;
      }

      var typeIntent = propCtx.Intents.OfType<TypeIntent>()
        .FirstOrDefault(ti => ti.Type is SchemaValueType.Integer or SchemaValueType.Number);

      if (typeIntent is null) {
        props.Properties.Add(name, propCtx);
        continue;
      }

      var exprNum
        = typeIntent.Type == SchemaValueType.Integer
          ? sgcIntegerExpression
          : sgcNumberExpression;

      props.Properties.Add(name, exprNum);
    }

    context.Intents.Add(new UnevaluatedPropertiesIntent(false));
    context.Intents.Add(new AdditionalPropertiesIntent(false));

    if (isDefType) {
      if (Mod.DefIdFields.TryGetValue(context.Type.Name, out var idFieldName)) {
        var exprIdFieldName = $"${idFieldName}";
        if (!props.Properties.ContainsKey(exprIdFieldName))
          props.Properties.Add(exprIdFieldName, sgcExplicitExpression);
      }

      context.Intents.Insert(0, new SchemaIntent(Mod.JsonSchemaDraft7));
      context.Intents.Insert(1, new IdIntent($"https://dw2mc.github.io/DW2ModLoader/def-{type.Name}.json"));
      var defs = context.Intents.OfType<DefinitionsIntent>().FirstOrDefault();
      if (defs is not null) {
        // move to end
        foreach (var (k, v) in Definitions)
          defs.Definitions.Add(k, v);
        var oldDefs = Definitions;
        Definitions = defs.Definitions;
        oldDefs.Clear();
      }
      else
        context.Intents.Add(new DefinitionsIntent(Definitions));
      props.Properties.Add("$where", sgcExplicitExpression); // for parser compatibility sake
      _rootTypeInit = true;
    }

#if DEBUG
    if (props.Properties.Count < minProps)
      throw new NotImplementedException("Woops!");
#endif
  }

}